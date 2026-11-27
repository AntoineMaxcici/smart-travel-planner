# My Trips and New Trip pages

After signing in, users land on **My Trips** (`MyTripsPage` + `MyTripsViewModel`). It lists their trips from
`GET /api/trips`, newest start date first, 20 at a time (more load as you scroll), with pull to refresh. Each card
shows the destination, dates, duration, interests, budget, status and a countdown. The page has a floating
**New trip** button, a **Delete** button on each card that asks for confirmation first, and **Sign out**.

**New Trip** (`NewTripPage` + `NewTripViewModel`) has a destination entry, start and end DatePickers, interest chips
loaded from `GET /api/interests`, and a budget Picker. It checks the same rules as the API before sending
(`TripRequest` validation, no start date in the past, at most 14 days, at least one interest), creates the trip with
`POST /api/trips`, and returns to My Trips, which reloads.

## Code

| Piece | Where |
|---|---|
| `ITripsApi`, `TripsApiException` | `TravelPlanner.Core/Services/TripsContracts.cs` |
| `TripsApi` (HTTP client for `/api/trips` and `/api/interests`) | `TravelPlanner.Core/Services/TripsApi.cs` |
| `FakeTripsApi` (in-memory sample trips) | `TravelPlanner.Core/Services/FakeTripsApi.cs` |
| `INavigationService`, `IDialogService` | `TravelPlanner.Core/Services/UiContracts.cs`, implemented in `TravelPlanner.Maui/Services` with Shell |
| View models | `TravelPlanner.Core/ViewModels/MyTripsViewModel.cs`, `NewTripViewModel.cs`, `TripCardViewModel.cs` |
| Pages | `TravelPlanner.Maui/Views/MyTripsPage.xaml`, `NewTripPage.xaml` |
| Shell routes | `mytrips` (root, `AppShell.xaml`) and `newtrip` (registered in `AppShell.xaml.cs`); names in `Services/AppRoutes.cs` |
| Styles | `TravelPlanner.Maui/Resources/Styles/Tourvex.xaml` and the palette in `Colors.xaml` |

`TripsApi` uses the app's authenticated `HttpClient`, so `BearerTokenHandler` adds the access token. A 401 becomes
`UnauthorizedSessionException`: the handler has already ended the session and the app returns to sign-in, so the
pages show no error for it. Other failures become `TripsApiException` with a message that is safe to show, including
the API's validation messages.

## Running without the API

`FakeTripsApi` returns four sample trips and the eight seeded interests, and supports create, save and delete until
the app closes. To use it, set the environment variable `TRAVELPLANNER_FAKE_TRIPS=1`, or change
`UseFakeTripsByDefault` to `true` in `TravelPlanner.Maui/Services/ApiSettings.cs`. You still sign in normally.

## Look

All pages follow the Tourvex template: teal `#2095AE`, navy `#0F2454`, body text `#5E6282`, light `#F4F5F8`.

**Sign in, sign up, confirm email and forgot password** use the template's home hero (`Controls/AuthLayout.cs`): navy
with the world-map pattern, the letter-spaced teal eyebrow, the big uppercase Barlow Condensed heading with its smaller
lowercase teal words, the three photo columns scrolling up endlessly (`PhotoMarquee`, 50s per loop, pauses on hover, navy
fade at the edges), the twinkling star, the plane gliding in from the right, the plane bobbing up and the turning compass
(`HeroDecorations`). The form sits in a white rounded card. Inputs are the template's outlined pills with a teal icon
(`PillField`, teal outline on hover and focus). Buttons are the template's arrow buttons (`ArrowButton`): `Teal` is
`butn-arrow` (Sign in, Create account, Confirm, Send code, Reset password) and `White` is `butn-arrow2` (the switch to
the other page, on the navy background). On hover the arrow slides down out of its circle while another slides in from the
top, and touch plays the same slide; while busy the circle shows a spinner. From 960px wide the hero uses the template's
two columns; narrower windows stack the text, form and photos.

**My Trips and New Trip** have a compact hero (`PageHero`: pattern, star, plane). Trip cards follow the template's tour
cards: photo on top with a status badge and countdown, location pin, title, interests, a "Duration" pill with a calendar
icon and the budget in teal; on hover the card lifts 10px and the photo zooms to 105%. New trip, Plan my first trip and
Create trip are arrow buttons, and New Trip uses the pill inputs.

`Controls/TvInputs.cs` removes the platforms' own entry, date picker and picker borders so only the pills show.

### Fonts

The template's fonts, in `TravelPlanner.Maui/Resources/Fonts` and registered in `MauiProgram`: Barlow Condensed (Medium
for headings, Regular for the teal accents, Italic for form-title accents, SemiBold) and Barlow Semi Condensed (Regular
and SemiBold). They are free under the SIL Open Font License (`TravelPlanner.Maui/Licenses/OFL-Barlow.txt`). The
template's icon font (Font Awesome Pro) needs its own commercial licence, so icons are drawn as vector paths
(`Controls/TvIcons.cs`).

### Photos

The template's photos are in `TravelPlanner.Maui/Resources/Images`, resized for the app (about 1.3 MB in total):
`tv_marquee_*.jpg` (hero columns), `tv_pattern_bg.png`, `tv_star.png`, `tv_flight_up.png`, `tv_flight_down.png`,
`tv_compass.png` (hero), and `trip_photo_1.jpg` to `trip_photo_4.jpg` (trip cards; a card uses the photo for its slot,
`TripCardViewModel.PhotoFile`). **Check your Envato licence before publishing the app:** many templates include demo
photos for the preview only. To swap a photo, replace the file and keep its name (lowercase, letters, digits and `_`).
