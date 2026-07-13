using System.Text;
using System.Windows.Controls;
using System.Xml;
using ICSharpCode.WpfDesign.Designer;
using ICSharpCode.WpfDesign.Designer.Xaml;

const string input = """
    <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Button x:Name="ActionButton" Width="120" Height="32" Content="Run" />
    </Grid>
    """;

var surface = new DesignSurface();
using (var reader = XmlReader.Create(new StringReader(input)))
{
    surface.LoadDesigner(reader, new XamlLoadSettings());
}

var context = surface.DesignContext
    ?? throw new InvalidOperationException("The design context was not created.");
var root = context.RootItem
    ?? throw new InvalidOperationException("The root design item was not created.");
if (root.Component is not Grid grid || grid.Children.Count != 1 || grid.Children[0] is not Button button)
{
    throw new InvalidOperationException("The typed Grid/Button design tree was not loaded.");
}

var buttonItem = context.Services.Component.GetDesignItem(button)
    ?? throw new InvalidOperationException("The button was not registered with the component service.");
buttonItem.Properties.GetProperty("Content").SetValue("Updated");

var output = new StringBuilder();
using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
{
    surface.SaveDesigner(writer);
}

string savedXaml = output.ToString();
if (!savedXaml.Contains("x:Name=\"ActionButton\"", StringComparison.Ordinal)
    || !savedXaml.Contains("Content=\"Updated\"", StringComparison.Ordinal))
{
    throw new InvalidOperationException("The edited design tree did not round-trip through XAML.");
}

surface.UnloadDesigner();
if (surface.DesignContext is not null)
{
    throw new InvalidOperationException("The design context remained attached after unload.");
}

Console.WriteLine("WPF designer LibreWPF smoke: root=Grid child=Button edit=Updated save=True unload=True");
