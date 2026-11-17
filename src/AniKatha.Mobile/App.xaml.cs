using Microsoft.Extensions.DependencyInjection;
namespace AniKatha.Mobile;
public partial class App : Application
{
 readonly IServiceProvider services;
 public App(IServiceProvider services)
 {
  InitializeComponent();
  this.services = services;
 }
 // Construct the page after Application resources and the window lifecycle exist.
 protected override Window CreateWindow(IActivationState? activationState) =>
  new(services.GetRequiredService<MainPage>());
}
