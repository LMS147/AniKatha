namespace AniKatha.Mobile;
public partial class App : Application
{
 readonly MainPage mainPage;
 public App(MainPage page) { InitializeComponent();mainPage=page; }
 protected override Window CreateWindow(IActivationState? activationState) => new(mainPage);
}
