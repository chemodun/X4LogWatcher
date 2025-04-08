using System;

namespace X4LogWatcher
{
  public static class Program
  {
    [STAThread]
    public static void Main()
    {
      var app = new App();
      app.Run();
    }
  }
}
