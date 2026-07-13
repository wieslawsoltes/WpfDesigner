using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using ICSharpCode.WpfDesign;
using ICSharpCode.WpfDesign.Adorners;
using ICSharpCode.WpfDesign.Designer;
using ICSharpCode.WpfDesign.Designer.Extensions;
using ICSharpCode.WpfDesign.Designer.Xaml;

const string input = """
    <Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            Width="400"
            Height="240">
      <Button x:Name="ActionButton"
              Width="120"
              Height="32"
              Content="Run"
              Canvas.Top="48"
              Canvas.Left="40" />
    </Canvas>
    """;

var application = Application.Current ?? new Application();
var surface = new DesignSurface();
var hostWindow = new Window
{
    Width = 640,
    Height = 480,
    Content = surface,
    ShowInTaskbar = false,
    WindowStyle = WindowStyle.None
};
using (var reader = XmlReader.Create(new StringReader(input)))
{
    surface.LoadDesigner(reader, new XamlLoadSettings());
}

hostWindow.Show();

var context = surface.DesignContext
    ?? throw new InvalidOperationException("The design context was not created.");
var root = context.RootItem
    ?? throw new InvalidOperationException("The root design item was not created.");
if (root.Component is not Canvas canvas || canvas.Children.Count != 1 || canvas.Children[0] is not Button button)
{
    throw new InvalidOperationException("The typed Canvas/Button design tree was not loaded.");
}

var buttonItem = context.Services.Component.GetDesignItem(button)
    ?? throw new InvalidOperationException("The button was not registered with the component service.");
var pointerTool = context.Services.Tool.PointerTool as IPointerTool
    ?? throw new InvalidOperationException("The tool service did not publish the typed pointer-tool input seam.");

surface.Width = 640;
surface.Height = 480;
surface.ApplyTemplate();
surface.Measure(new Size(surface.Width, surface.Height));
surface.Arrange(new Rect(0, 0, surface.Width, surface.Height));
surface.UpdateLayout();

string originalXaml = Save(surface);
if (pointerTool.TryStartGesture(
        surface.DesignPanel,
        new Point(double.NaN, 0),
        1,
        SelectionTypes.Primary) is not null
    || pointerTool.TryStartGesture(
        surface.DesignPanel,
        new Point(-8, -8),
        1,
        SelectionTypes.Primary) is not null
    || context.Services.Selection.SelectionCount != 0
    || surface.CanUndo()
    || !string.Equals(Save(surface), originalXaml, StringComparison.Ordinal))
{
    throw new InvalidOperationException("A non-finite or missed pointer gesture did not fail closed.");
}

Point buttonCenter = button.TranslatePoint(
    new Point(button.ActualWidth / 2, button.ActualHeight / 2),
    surface.DesignPanel);
var hit = surface.DesignPanel.HitTest(
    buttonCenter,
    false,
    true,
    HitTestType.ElementSelection);
if (!ReferenceEquals(hit.ModelHit, buttonItem))
{
    throw new InvalidOperationException(
        "The real design panel did not hit-test the existing button."
        + " center=" + buttonCenter
        + " buttonSize=" + button.ActualWidth + "x" + button.ActualHeight
        + " panelSize=" + surface.DesignPanel.RenderSize
        + " model=" + (hit.ModelHit?.ComponentType.FullName ?? "<null>")
        + " visual=" + (hit.VisualHit?.GetType().FullName ?? "<null>"));
}

var gesture = pointerTool.TryStartGesture(
        surface.DesignPanel,
        buttonCenter,
        1,
        SelectionTypes.Primary)
    ?? throw new InvalidOperationException("The pointer tool did not start a gesture on the hit button.");
if (!ReferenceEquals(gesture.HitItem, buttonItem)
    || !ReferenceEquals(context.Services.Selection.PrimarySelection, buttonItem)
    || context.Services.Selection.SelectionCount != 1)
{
    throw new InvalidOperationException("The pointer gesture did not select the hit button.");
}

var resizeExtension = buttonItem.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault()
    ?? throw new InvalidOperationException("Selection did not install the typed resize-thumb extension.");
var selectionAdorners = buttonItem.Extensions
    .OfType<SelectionAdornerProvider>()
    .SelectMany(provider => provider.Adorners)
    .ToArray();
if (resizeExtension.Adorners.Count == 0
    || selectionAdorners.Length == 0
    || !selectionAdorners.All(surface.DesignPanel.Adorners.Contains))
{
    throw new InvalidOperationException("Selection adorners were not published by the real design panel.");
}

var movedPosition = buttonCenter + new Vector(24, 18);
if (!gesture.Move(movedPosition) || !gesture.HasMoved)
{
    gesture.Cancel();
    throw new InvalidOperationException("The typed pointer gesture did not start a placement move.");
}
gesture.Complete();
if (gesture.IsActive || !surface.CanUndo())
{
    throw new InvalidOperationException("The pointer move did not commit one undoable placement operation.");
}

string movedXaml = Save(surface);
if (string.Equals(movedXaml, originalXaml, StringComparison.Ordinal)
    || Canvas.GetLeft(button) != 64
    || Canvas.GetTop(button) != 66)
{
    throw new InvalidOperationException("The pointer move did not produce the expected Canvas position and XAML delta.");
}

surface.Undo();
string undoneXaml = Save(surface);
if (!string.Equals(undoneXaml, originalXaml, StringComparison.Ordinal)
    || surface.CanUndo()
    || !surface.CanRedo())
{
    throw new InvalidOperationException(
        "Undo did not restore the exact source in one transaction."
        + " exact=" + string.Equals(undoneXaml, originalXaml, StringComparison.Ordinal)
        + " canUndo=" + surface.CanUndo()
        + " canRedo=" + surface.CanRedo()
        + Environment.NewLine + "original=" + originalXaml
        + Environment.NewLine + "undone=" + undoneXaml);
}

surface.Redo();
if (!string.Equals(Save(surface), movedXaml, StringComparison.Ordinal))
{
    throw new InvalidOperationException("Redo did not restore the exact pointer-move XAML.");
}

surface.Undo();
if (!string.Equals(Save(surface), originalXaml, StringComparison.Ordinal))
{
    throw new InvalidOperationException("The final undo did not restore the exact original source.");
}

surface.UnloadDesigner();
if (surface.DesignContext is not null)
{
    throw new InvalidOperationException("The design context remained attached after unload.");
}
hostWindow.Close();

Console.WriteLine(
    "WPF designer LibreWPF smoke: root=Canvas child=Button"
    + " pointerHit=True pointerSelection=True move=True selectionAdorners=True"
    + " missFailClosed=True xamlDelta=True undo=True redo=True restore=True unload=True");

static string Save(DesignSurface surface)
{
    var output = new StringBuilder();
    using (var writer = XmlWriter.Create(
        output,
        new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
    {
        surface.SaveDesigner(writer);
    }

    return output.ToString();
}
