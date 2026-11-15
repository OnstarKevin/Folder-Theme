Add-Type -WarningAction SilentlyContinue -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @"
using System;
using System.Windows.Forms;
using System.Drawing;
public sealed class IslandInputReceiver : Form {
  private int expectedMessage;
  public IslandInputReceiver() {
    FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
    Bounds = new Rectangle(80, 180, 420, 160); TopMost = true; ShowInTaskbar = false;
    BackColor = Color.SlateGray;
  }
  protected override bool ShowWithoutActivation { get { return true; } }
  protected override void OnShown(EventArgs e) {
    base.OnShown(e); Console.WriteLine("READY " + Handle.ToInt64()); Console.Out.Flush();
  }
  protected override void WndProc(ref Message m) {
    if (m.Msg == 0x8001) { expectedMessage = m.WParam.ToInt32(); return; }
    if (m.Msg == expectedMessage && (m.Msg == 0x201 || m.Msg == 0x203 || m.Msg == 0x20A)) {
      expectedMessage = 0;
      Console.WriteLine("INPUT " + m.Msg); Console.Out.Flush();
    }
    base.WndProc(ref m);
  }
}
"@
[System.Windows.Forms.Application]::Run((New-Object IslandInputReceiver))
