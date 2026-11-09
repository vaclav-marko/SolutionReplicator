using System.Windows;

namespace SapVariantReplacer;

public partial class App : Application
{
}

// InvertBoolConverter moved to Converters/InvertBoolConverter.cs so it is available
// when this project is built as a class library (App.xaml.cs is excluded then).
