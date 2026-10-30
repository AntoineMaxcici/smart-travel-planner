namespace TravelPlanner.Maui
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage)
        {
            InitializeComponent();
            HomeContent.Content = mainPage;
        }
    }
}
