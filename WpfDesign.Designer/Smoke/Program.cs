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

const string resizeInput = """
    <Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            Width="400"
            Height="240">
      <Button x:Name="ResizeButton"
              MinWidth="80"
              MaxWidth="150"
              MinHeight="24"
              MaxHeight="60"
              Content="Resize"
              Canvas.Top="48"
              Canvas.Left="40"
              Height="32"
              Width="120" />
    </Canvas>
    """;

const string gridResizeInput = """
    <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
          Width="300"
          Height="180">
      <Button x:Name="StretchButton"
              Content="Stretch"
              HorizontalAlignment="Stretch"
              VerticalAlignment="Stretch"
              Margin="20" />
    </Grid>
    """;

const string unsupportedResizeInput = """
    <Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            Width="320"
            Height="180">
      <ContentPresenter x:Name="UnsupportedPresenter"
                        Canvas.Left="20"
                        Canvas.Top="20"
                        Width="240"
                        Height="120">
        <ContentPresenter.Content>
          <Button x:Name="UnsupportedButton"
                  Content="Unsupported"
                  Height="30"
                  Width="100" />
        </ContentPresenter.Content>
      </ContentPresenter>
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
    throw new InvalidOperationException("The pointer design context remained attached after unload.");
}

var resizeSurface = new DesignSurface();
hostWindow.Content = resizeSurface;
using (var reader = XmlReader.Create(new StringReader(resizeInput)))
{
    resizeSurface.LoadDesigner(reader, new XamlLoadSettings());
}

var resizeContext = resizeSurface.DesignContext
    ?? throw new InvalidOperationException("The resize design context was not created.");
var resizeRoot = resizeContext.RootItem
    ?? throw new InvalidOperationException("The resize root design item was not created.");
if (resizeRoot.Component is not Canvas resizeCanvas
    || resizeCanvas.Children.Count != 1
    || resizeCanvas.Children[0] is not Button resizeButton)
{
    throw new InvalidOperationException("The typed resize Canvas/Button design tree was not loaded.");
}

var resizeButtonItem = resizeContext.Services.Component.GetDesignItem(resizeButton)
    ?? throw new InvalidOperationException("The resize button was not registered with the component service.");
resizeSurface.Width = 640;
resizeSurface.Height = 480;
resizeSurface.ApplyTemplate();
resizeSurface.Measure(new Size(resizeSurface.Width, resizeSurface.Height));
resizeSurface.Arrange(new Rect(0, 0, resizeSurface.Width, resizeSurface.Height));
resizeSurface.UpdateLayout();
resizeContext.Services.Selection.SetSelectedComponents(
    new[] { resizeButtonItem },
    SelectionTypes.Primary);

string resizeOriginalXaml = Save(resizeSurface);
var resizeSurfaceExtension = resizeButtonItem.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault()
    ?? throw new InvalidOperationException("Selection did not install resize thumbs on the resize surface.");
if (resizeSurfaceExtension.Adorners.Count == 0
    || !resizeSurfaceExtension.Adorners.All(resizeSurface.DesignPanel.Adorners.Contains)
    || resizeSurfaceExtension.TryStartGesture(PlacementAlignment.Center) is not null)
{
    throw new InvalidOperationException("Resize adorners were missing or the center alignment did not fail closed.");
}

var invalidResize = resizeSurfaceExtension.TryStartGesture(PlacementAlignment.BottomRight)
    ?? throw new InvalidOperationException("The typed resize gesture was unavailable for the bottom-right handle.");
if (resizeSurfaceExtension.TryStartGesture(PlacementAlignment.Right) is not null
    || invalidResize.Update(new Vector(double.NaN, 1), false)
    || invalidResize.HasResized)
{
    invalidResize.Cancel();
    throw new InvalidOperationException("A reentrant or non-finite resize gesture did not fail closed.");
}
invalidResize.Complete();
if (resizeSurface.CanUndo()
    || !string.Equals(Save(resizeSurface), resizeOriginalXaml, StringComparison.Ordinal))
{
    throw new InvalidOperationException("A no-op resize gesture created an undo unit or changed XAML.");
}

resizeSurfaceExtension = resizeButtonItem.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault()
    ?? throw new InvalidOperationException("The resize-thumb extension was not restored after the no-op gesture.");
var resizeGesture = resizeSurfaceExtension.TryStartGesture(PlacementAlignment.BottomRight)
    ?? throw new InvalidOperationException("The constrained resize gesture did not start.");
if (!resizeGesture.Update(new Vector(100, 100), false) || !resizeGesture.HasResized)
{
    resizeGesture.Cancel();
    throw new InvalidOperationException("The typed resize gesture did not apply its cumulative delta.");
}
resizeGesture.Complete();
if (resizeGesture.IsActive
    || resizeButton.Width != 150
    || resizeButton.Height != 60
    || Canvas.GetLeft(resizeButton) != 40
    || Canvas.GetTop(resizeButton) != 48
    || !resizeSurface.CanUndo())
{
    throw new InvalidOperationException(
        "The bottom-right resize did not honor max-size and fixed-anchor constraints."
        + " width=" + resizeButton.Width
        + " height=" + resizeButton.Height
        + " left=" + Canvas.GetLeft(resizeButton)
        + " top=" + Canvas.GetTop(resizeButton));
}

string resizedXaml = Save(resizeSurface);
if (string.Equals(resizedXaml, resizeOriginalXaml, StringComparison.Ordinal))
{
    throw new InvalidOperationException("The committed resize did not change XAML.");
}

resizeSurface.Undo();
string resizeUndoneXaml = Save(resizeSurface);
if (!string.Equals(resizeUndoneXaml, resizeOriginalXaml, StringComparison.Ordinal)
    || resizeSurface.CanUndo()
    || !resizeSurface.CanRedo())
{
    throw new InvalidOperationException(
        "One undo did not restore the exact pre-resize placement."
        + " exact=" + string.Equals(resizeUndoneXaml, resizeOriginalXaml, StringComparison.Ordinal)
        + " canUndo=" + resizeSurface.CanUndo()
        + " canRedo=" + resizeSurface.CanRedo()
        + Environment.NewLine + "original=" + resizeOriginalXaml
        + Environment.NewLine + "undone=" + resizeUndoneXaml);
}
resizeSurface.Redo();
if (!string.Equals(Save(resizeSurface), resizedXaml, StringComparison.Ordinal))
{
    throw new InvalidOperationException("Redo did not restore the exact constrained-resize XAML.");
}
resizeSurface.Undo();
if (!string.Equals(Save(resizeSurface), resizeOriginalXaml, StringComparison.Ordinal))
{
    throw new InvalidOperationException("The resize redo cleanup did not restore the original source.");
}

resizeSurfaceExtension = resizeButtonItem.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault()
    ?? throw new InvalidOperationException("The resize-thumb extension was not restored for cancel coverage.");
var canceledResize = resizeSurfaceExtension.TryStartGesture(PlacementAlignment.Left)
    ?? throw new InvalidOperationException("The left resize gesture did not start.");
if (!canceledResize.Update(new Vector(100, 0), false)
    || resizeButton.Width != 80
    || resizeButton.Height != 32
    || Canvas.GetLeft(resizeButton) != 80
    || Canvas.GetTop(resizeButton) != 48)
{
    canceledResize.Cancel();
    throw new InvalidOperationException(
        "The left resize did not preserve its right anchor while clamping to MinWidth."
        + " width=" + resizeButton.Width
        + " height=" + resizeButton.Height
        + " left=" + Canvas.GetLeft(resizeButton)
        + " top=" + Canvas.GetTop(resizeButton));
}
canceledResize.Cancel();
if (canceledResize.IsActive
    || resizeSurface.CanUndo()
    || resizeButton.Width != 120
    || resizeButton.Height != 32
    || Canvas.GetLeft(resizeButton) != 40
    || Canvas.GetTop(resizeButton) != 48)
{
    throw new InvalidOperationException(
        "Cancel did not restore the pre-resize placement without undo state."
        + " active=" + canceledResize.IsActive
        + " canUndo=" + resizeSurface.CanUndo()
        + " width=" + resizeButton.Width
        + " height=" + resizeButton.Height
        + " left=" + Canvas.GetLeft(resizeButton)
        + " top=" + Canvas.GetTop(resizeButton));
}

resizeSurface.UnloadDesigner();
if (resizeSurface.DesignContext is not null)
{
    throw new InvalidOperationException("The resize design context remained attached after unload.");
}

var gridSurface = new DesignSurface();
hostWindow.Content = gridSurface;
using (var reader = XmlReader.Create(new StringReader(gridResizeInput)))
{
    gridSurface.LoadDesigner(reader, new XamlLoadSettings());
}

var gridContext = gridSurface.DesignContext
    ?? throw new InvalidOperationException("The Grid resize design context was not created.");
var gridRoot = gridContext.RootItem
    ?? throw new InvalidOperationException("The Grid resize root item was not created.");
if (gridRoot.Component is not Grid grid
    || grid.Children.Count != 1
    || grid.Children[0] is not Button stretchButton)
{
    throw new InvalidOperationException("The typed Grid/stretch-Button design tree was not loaded.");
}

var stretchButtonItem = gridContext.Services.Component.GetDesignItem(stretchButton)
    ?? throw new InvalidOperationException("The stretch button was not registered with the component service.");
gridSurface.Width = 640;
gridSurface.Height = 480;
gridSurface.ApplyTemplate();
gridSurface.Measure(new Size(gridSurface.Width, gridSurface.Height));
gridSurface.Arrange(new Rect(0, 0, gridSurface.Width, gridSurface.Height));
gridSurface.UpdateLayout();
gridContext.Services.Selection.SetSelectedComponents(
    new[] { stretchButtonItem },
    SelectionTypes.Primary);

string gridOriginalXaml = Save(gridSurface);
var gridResizeExtension = stretchButtonItem.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault()
    ?? throw new InvalidOperationException("The Grid stretch button did not install resize thumbs.");
var gridResize = gridResizeExtension.TryStartGesture(PlacementAlignment.Right)
    ?? throw new InvalidOperationException("The Grid stretch resize gesture did not start.");
if (!gridResize.Update(new Vector(-40, 0), false)
    || !gridResize.HasResized
    || !double.IsNaN(stretchButton.Width)
    || !double.IsNaN(stretchButton.Height)
    || stretchButton.HorizontalAlignment != HorizontalAlignment.Stretch
    || stretchButton.VerticalAlignment != VerticalAlignment.Stretch
    || stretchButton.Margin == new Thickness(20)
    || string.Equals(Save(gridSurface), gridOriginalXaml, StringComparison.Ordinal))
{
    gridResize.Cancel();
    throw new InvalidOperationException("Grid resize did not preserve stretch alignment and unset dimensions.");
}
gridResize.Complete();
string gridResizedXaml = Save(gridSurface);
if (gridResize.IsActive
    || !gridSurface.CanUndo()
    || !double.IsNaN(stretchButton.Width)
    || !double.IsNaN(stretchButton.Height)
    || stretchButton.HorizontalAlignment != HorizontalAlignment.Stretch
    || stretchButton.VerticalAlignment != VerticalAlignment.Stretch
    || stretchButton.Margin == new Thickness(20)
    || string.Equals(gridResizedXaml, gridOriginalXaml, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Grid stretch resize did not commit one undoable layout transaction."
        + " active=" + gridResize.IsActive
        + " canUndo=" + gridSurface.CanUndo()
        + " width=" + stretchButton.Width
        + " height=" + stretchButton.Height
        + " horizontal=" + stretchButton.HorizontalAlignment
        + " vertical=" + stretchButton.VerticalAlignment
        + " margin=" + stretchButton.Margin);
}
gridSurface.Undo();
string gridUndoneXaml = Save(gridSurface);
if (!string.Equals(gridUndoneXaml, gridOriginalXaml, StringComparison.Ordinal)
    || gridSurface.CanUndo()
    || !gridSurface.CanRedo()
    || stretchButton.Margin != new Thickness(20))
{
    throw new InvalidOperationException(
        "Grid stretch resize undo did not restore the exact original XAML."
        + " canUndo=" + gridSurface.CanUndo()
        + " canRedo=" + gridSurface.CanRedo()
        + " margin=" + stretchButton.Margin
        + Environment.NewLine + "original=" + gridOriginalXaml
        + Environment.NewLine + "undone=" + gridUndoneXaml);
}
gridSurface.Redo();
if (!string.Equals(Save(gridSurface), gridResizedXaml, StringComparison.Ordinal))
{
    throw new InvalidOperationException("Grid stretch resize redo did not restore the exact committed XAML.");
}
gridSurface.Undo();

gridResizeExtension = stretchButtonItem.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault()
    ?? throw new InvalidOperationException("The Grid resize-thumb extension was not restored for cancel coverage.");
var canceledGridResize = gridResizeExtension.TryStartGesture(PlacementAlignment.Bottom)
    ?? throw new InvalidOperationException("The Grid stretch cancel gesture did not start.");
if (!canceledGridResize.Update(new Vector(0, -30), false)
    || stretchButton.Margin == new Thickness(20))
{
    canceledGridResize.Cancel();
    throw new InvalidOperationException("The Grid stretch cancel gesture did not mutate layout state.");
}
canceledGridResize.Cancel();
if (canceledGridResize.IsActive
    || gridSurface.CanUndo()
    || !string.Equals(Save(gridSurface), gridOriginalXaml, StringComparison.Ordinal)
    || stretchButton.Margin != new Thickness(20))
{
    throw new InvalidOperationException("Grid stretch cancel did not restore exact XAML without an undo unit.");
}

gridSurface.UnloadDesigner();
if (gridSurface.DesignContext is not null)
{
    throw new InvalidOperationException("The Grid resize design context remained attached after unload.");
}

var unsupportedSurface = new DesignSurface();
hostWindow.Content = unsupportedSurface;
using (var reader = XmlReader.Create(new StringReader(unsupportedResizeInput)))
{
    unsupportedSurface.LoadDesigner(reader, new XamlLoadSettings());
}

var unsupportedContext = unsupportedSurface.DesignContext
    ?? throw new InvalidOperationException("The unsupported resize design context was not created.");
var unsupportedRoot = unsupportedContext.RootItem
    ?? throw new InvalidOperationException("The unsupported resize root item was not created.");
if (unsupportedRoot.Component is not Canvas unsupportedCanvas
    || unsupportedCanvas.Children.Count != 1
    || unsupportedCanvas.Children[0] is not ContentPresenter presenter
    || presenter.Content is not Button unsupportedButton)
{
    throw new InvalidOperationException(
        "The typed unsupported-parent/Button design tree was not loaded."
        + " root=" + unsupportedRoot.Component?.GetType().FullName
        + " childCount=" + (unsupportedRoot.Component is Canvas actualCanvas ? actualCanvas.Children.Count : -1)
        + " child=" + (unsupportedRoot.Component is Canvas canvasWithChild && canvasWithChild.Children.Count > 0
            ? canvasWithChild.Children[0].GetType().FullName
            : "<none>"));
}

var unsupportedPresenterItem = unsupportedContext.Services.Component.GetDesignItem(presenter)
    ?? throw new InvalidOperationException("The unsupported ContentPresenter was not registered.");
var unsupportedButtonItem = unsupportedContext.Services.Component.GetDesignItem(unsupportedButton)
    ?? throw new InvalidOperationException("The unsupported-parent button was not registered.");
unsupportedSurface.Width = 640;
unsupportedSurface.Height = 480;
unsupportedSurface.ApplyTemplate();
unsupportedSurface.Measure(new Size(unsupportedSurface.Width, unsupportedSurface.Height));
unsupportedSurface.Arrange(new Rect(0, 0, unsupportedSurface.Width, unsupportedSurface.Height));
unsupportedSurface.UpdateLayout();
unsupportedContext.Services.Selection.SetSelectedComponents(
    new[] { unsupportedButtonItem },
    SelectionTypes.Primary);

string unsupportedOriginalXaml = Save(unsupportedSurface);
var unsupportedResizeExtension = unsupportedButtonItem.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault()
    ?? throw new InvalidOperationException("The unsupported-parent button did not install its selection extension.");
if (unsupportedResizeExtension.TryStartGesture(PlacementAlignment.Right) is not null
    || unsupportedResizeExtension.IsResizing
    || unsupportedSurface.CanUndo()
    || !string.Equals(Save(unsupportedSurface), unsupportedOriginalXaml, StringComparison.Ordinal))
{
    throw new InvalidOperationException("A missing placement behavior did not fail closed without mutation.");
}

unsupportedContext.Services.Selection.SetSelectedComponents(null);
var throwingBehavior = new ThrowingPlacementBehavior();
unsupportedPresenterItem.AddBehavior(typeof(IPlacementBehavior), throwingBehavior);
unsupportedContext.Services.Selection.SetSelectedComponents(
    new[] { unsupportedButtonItem },
    SelectionTypes.Primary);
unsupportedResizeExtension = unsupportedButtonItem.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault()
    ?? throw new InvalidOperationException("The throwing-behavior button did not reinstall resize thumbs.");
var failingResize = unsupportedResizeExtension.TryStartGesture(PlacementAlignment.Right)
    ?? throw new InvalidOperationException("The failure-cleanup resize gesture did not start.");
bool updateFailed = false;
try
{
    failingResize.Update(new Vector(10, 0), false);
}
catch (InvalidOperationException exception)
    when (exception.Message == ThrowingPlacementBehavior.FailureMessage)
{
    updateFailed = true;
}
if (!updateFailed
    || failingResize.IsActive
    || unsupportedResizeExtension.IsResizing
    || throwingBehavior.EndCount != 1
    || unsupportedSurface.CanUndo()
    || unsupportedButton.Width != 100
    || unsupportedButton.Height != 30
    || !string.Equals(Save(unsupportedSurface), unsupportedOriginalXaml, StringComparison.Ordinal))
{
    if (failingResize.IsActive)
    {
        failingResize.Cancel();
    }
    throw new InvalidOperationException(
        "A failed resize update did not abort and clean up its gesture state."
        + " updateFailed=" + updateFailed
        + " gestureActive=" + failingResize.IsActive
        + " extensionActive=" + unsupportedResizeExtension.IsResizing
        + " endCount=" + throwingBehavior.EndCount
        + " canUndo=" + unsupportedSurface.CanUndo()
        + " width=" + unsupportedButton.Width
        + " height=" + unsupportedButton.Height
        + " exactXaml=" + string.Equals(Save(unsupportedSurface), unsupportedOriginalXaml, StringComparison.Ordinal)
        + Environment.NewLine + "original=" + unsupportedOriginalXaml
        + Environment.NewLine + "restored=" + Save(unsupportedSurface));
}

unsupportedSurface.UnloadDesigner();
if (unsupportedSurface.DesignContext is not null)
{
    throw new InvalidOperationException("The unsupported resize design context remained attached after unload.");
}
hostWindow.Close();

Console.WriteLine(
    "WPF designer LibreWPF smoke: root=Canvas child=Button"
    + " pointerHit=True pointerSelection=True move=True selectionAdorners=True"
    + " missFailClosed=True resizeFeed=True resizeFailClosed=True"
    + " minMax=True leftAnchor=True resizeUndo=True resizeRedo=True"
    + " resizeCancel=True gridStretch=True missingBehaviorFailClosed=True"
    + " updateFailureCleanup=True xamlDelta=True restore=True unload=True");

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

sealed class ThrowingPlacementBehavior : IPlacementBehavior
{
    internal const string FailureMessage = "Injected resize placement failure.";

    internal int EndCount { get; private set; }

    public bool CanPlace(
        IEnumerable<DesignItem> childItems,
        PlacementType type,
        PlacementAlignment position)
    {
        return type == PlacementType.Resize && position != PlacementAlignment.Center;
    }

    public void BeginPlacement(PlacementOperation operation)
    {
    }

    public void EndPlacement(PlacementOperation operation)
    {
        EndCount++;
    }

    public Rect GetPosition(PlacementOperation operation, DesignItem child)
    {
        return new Rect(new Point(), PlacementOperation.GetRealElementSize(child.View));
    }

    public void BeforeSetPosition(PlacementOperation operation)
    {
    }

    public void SetPosition(PlacementInformation info)
    {
        info.Item.Properties[FrameworkElement.WidthProperty].SetValue(info.Bounds.Width);
        info.Item.Properties[FrameworkElement.HeightProperty].SetValue(info.Bounds.Height);
        throw new InvalidOperationException(FailureMessage);
    }

    public bool CanLeaveContainer(PlacementOperation operation)
    {
        return false;
    }

    public void LeaveContainer(PlacementOperation operation)
    {
        throw new NotSupportedException();
    }

    public bool CanEnterContainer(PlacementOperation operation, bool shouldAlwaysEnter)
    {
        return false;
    }

    public void EnterContainer(PlacementOperation operation)
    {
        throw new NotSupportedException();
    }

    public Point PlacePoint(Point point)
    {
        return point;
    }
}
