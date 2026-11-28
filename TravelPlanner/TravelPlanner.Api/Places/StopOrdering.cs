using TravelPlanner.Api.Data.Entities;

namespace TravelPlanner.Api.Places;

// PB-6: orders the stops of a day so the traveler isn't zig-zagging across the city.
//
// Rules:
// 1. Morning, afternoon and evening stay in that order. Stops only move inside their own time slot.
// 2. The day's first stop stays first. After that, the next stop is always the nearest remaining one
//    in the same slot ("nearest neighbour"). Each slot continues from the last stop of the slot before.
// 3. A slot is only changed if the new route is shorter by at least MinimumSavingKm.
// 4. If the stops have times, they are re-laid so they still don't overlap and stay in their slot.
//    If that isn't possible, the AI's order for the whole day is kept.
// 5. Days where any stop has no coordinates are left exactly as the AI suggested.
public static class StopOrdering
{
    public const double MinimumSavingKm = 0.2;

    private sealed record Planned(ItineraryStop Stop, TimeOnly? Start, TimeOnly? End, int? TravelMinutes);

    // Returns true when the day's order changed. The day's Stops list is sorted to match the new SortOrder.
    public static bool Optimize(ItineraryDay day)
    {
        var original = day.Stops.OrderBy(s => s.SortOrder).ToList();
        if (original.Count < 3 || original.Any(s => s.DisplayLatitude is null || s.DisplayLongitude is null))
            return false;

        var proposed = new List<ItineraryStop>(original.Count);
        ItineraryStop? previous = null;
        foreach (var slot in original.GroupBy(s => s.TimeSlot))
        {
            var current = slot.ToList();
            var candidate = NearestNeighbour(current, previous);
            var better = RouteKm(previous, candidate) + MinimumSavingKm < RouteKm(previous, current);
            proposed.AddRange(better ? candidate : current);
            previous = proposed[^1];
        }
        if (proposed.SequenceEqual(original)) return false;

        var plan = Schedule(original, proposed);
        if (plan is null) return false;

        for (var index = 0; index < plan.Count; index++)
        {
            var (stop, start, end, travel) = plan[index];
            stop.SortOrder = index + 1;
            stop.StartTimeLocal = start;
            stop.EndTimeLocal = end;
            stop.TravelMinutesFromPrevious = travel;
        }
        // Sort in place: replacing the list would make EF Core think the stops were removed.
        day.Stops.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        return true;
    }

    // Straight-line distance between two points on Earth (haversine formula), in kilometres.
    public static double DistanceKm(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double EarthRadiusKm = 6371.0;
        static double Radians(double degrees) => degrees * Math.PI / 180;
        var dLat = Radians(latitude2 - latitude1);
        var dLon = Radians(longitude2 - longitude1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Radians(latitude1)) * Math.Cos(Radians(latitude2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    // Rough door-to-door estimate, rounded up to 5 minutes: streets are ~30% longer than a straight line,
    // walk up to 1.5 km at 4.5 km/h, otherwise ride at 18 km/h plus 10 minutes of waiting.
    public static int EstimateTravelMinutes(ItineraryStop from, ItineraryStop to)
    {
        var km = Distance(from, to) * 1.3;
        var minutes = km <= 1.5 ? km / 4.5 * 60 : 10 + km / 18 * 60;
        return Math.Max(5, (int)Math.Ceiling(minutes / 5) * 5);
    }

    private static double Distance(ItineraryStop a, ItineraryStop b) =>
        DistanceKm(a.DisplayLatitude!.Value, a.DisplayLongitude!.Value, b.DisplayLatitude!.Value, b.DisplayLongitude!.Value);

    private static List<ItineraryStop> NearestNeighbour(List<ItineraryStop> stops, ItineraryStop? previous)
    {
        var remaining = stops.ToList();
        var route = new List<ItineraryStop>(stops.Count);
        if (previous is null)
        {
            // Start of the day: keep the AI's first stop as the starting point.
            route.Add(remaining[0]);
            remaining.RemoveAt(0);
            previous = route[0];
        }
        while (remaining.Count > 0)
        {
            var from = previous;
            var next = remaining.MinBy(stop => Distance(from, stop))!;
            route.Add(next);
            remaining.Remove(next);
            previous = next;
        }
        return route;
    }

    private static double RouteKm(ItineraryStop? previous, List<ItineraryStop> route)
    {
        var total = 0.0;
        foreach (var stop in route)
        {
            if (previous is not null) total += Distance(previous, stop);
            previous = stop;
        }
        return total;
    }

    // Works out times and travel minutes for the new order, or returns null if they can't fit.
    private static List<Planned>? Schedule(List<ItineraryStop> original, List<ItineraryStop> proposed)
    {
        var timed = original.Count(s => s.StartTimeLocal.HasValue && s.EndTimeLocal.HasValue);
        if (timed != 0 && timed != original.Count) return null;

        var plan = new List<Planned>(proposed.Count);
        TimeSpan? previousEnd = null;
        for (var index = 0; index < proposed.Count; index++)
        {
            var stop = proposed[index];
            var before = index == 0 ? null : proposed[index - 1];
            var originalIndex = original.IndexOf(stop);
            var originalBefore = originalIndex == 0 ? null : original[originalIndex - 1];
            // Keep the AI's travel estimate when the stop still follows the same stop; otherwise estimate it.
            var travel = before is null || before == originalBefore
                ? stop.TravelMinutesFromPrevious
                : EstimateTravelMinutes(before, stop);

            if (timed == 0)
            {
                plan.Add(new Planned(stop, null, null, travel));
                continue;
            }

            // Each position keeps its original start time as the earliest start, so the day's shape
            // (when the morning, afternoon and evening begin) stays as the AI planned it.
            var duration = stop.EndTimeLocal!.Value - stop.StartTimeLocal!.Value;
            var start = original[index].StartTimeLocal!.Value.ToTimeSpan();
            if (previousEnd is TimeSpan end && end + TimeSpan.FromMinutes(travel ?? 0) > start)
                start = end + TimeSpan.FromMinutes(travel ?? 0);
            var finish = start + duration;
            if (duration <= TimeSpan.Zero || finish >= TimeSpan.FromDays(1) || SlotFor(start) != stop.TimeSlot)
                return null;
            plan.Add(new Planned(stop, TimeOnly.FromTimeSpan(start), TimeOnly.FromTimeSpan(finish), travel));
            previousEnd = finish;
        }
        return plan;
    }

    // Same boundaries as ItineraryValidation: before noon is morning, before 6 PM is afternoon.
    private static TimeSlot SlotFor(TimeSpan start) => start.Hours < 12 ? TimeSlot.Morning
        : start.Hours < 18 ? TimeSlot.Afternoon : TimeSlot.Evening;
}
