using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;

using ETS2LA.State;
using ETS2LA.Overlay;

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace ETS2LA.UI.Views.Settings;

public partial class DisplaySettings : UserControl, INotifyPropertyChanged
{
    // TabStripItemHandler comes from AssistanceSettings.
    // TODO: Refactor elements to be global and reusable instead of copy pasting!
    public ObservableCollection<TabStripItemHandler> DisplayUnitOptions { get; } = new();
    public int SelectedDisplayUnitOption { get; set; }

    public bool IsUsingScientificUnits => StateSettingsHandler.Current.GetSettings().DisplayUnits == Units.Scientific;

    public bool ChangedSupportMultipleViewports { get; private set; } = false;
    public bool SupportMultipleViewports
    {
        get => OverlaySettingsHandler.Current.GetSettings().SupportMultipleViewports;
        set
        {
            if (OverlaySettingsHandler.Current.GetSettings().SupportMultipleViewports != value)
            {
                OverlaySettingsHandler.Current.GetSettings().SupportMultipleViewports = value;
                ChangedSupportMultipleViewports = true;
                OverlaySettingsHandler.Current.Save();
            }
            OnPropertyChanged(nameof(ChangedSupportMultipleViewports));
            OnPropertyChanged(nameof(SupportMultipleViewports));
        }
    }

    public bool LimitOverlayFramerate
    {
        get => OverlaySettingsHandler.Current.GetSettings().LimitFramerate;
        set
        {
            if (OverlaySettingsHandler.Current.GetSettings().LimitFramerate != value)
            {
                OverlaySettingsHandler.Current.GetSettings().LimitFramerate = value;
                OverlaySettingsHandler.Current.Save();
            }
            OnPropertyChanged(nameof(LimitOverlayFramerate));
        }
    }

    public int MaxOverlayFramerate
    {
        get => OverlaySettingsHandler.Current.GetSettings().MaxFramerate;
        set
        {
            if (OverlaySettingsHandler.Current.GetSettings().MaxFramerate != value)
            {
                OverlaySettingsHandler.Current.GetSettings().MaxFramerate = value;
                OverlaySettingsHandler.Current.Save();
            }
            OnPropertyChanged(nameof(MaxOverlayFramerate));
        }
    }

    public bool RenderAR
    {
        get => OverlaySettingsHandler.Current.GetSettings().RenderAR;
        set
        {
            if (OverlaySettingsHandler.Current.GetSettings().RenderAR != value)
            {
                OverlaySettingsHandler.Current.GetSettings().RenderAR = value;
                OverlaySettingsHandler.Current.Save();
            }
            OnPropertyChanged(nameof(RenderAR));
        }
    }

    public bool SimplifiedGraphics
    {
        get => OverlaySettingsHandler.Current.GetSettings().SimplifiedGraphics;
        set
        {
            if (OverlaySettingsHandler.Current.GetSettings().SimplifiedGraphics != value)
            {
                OverlaySettingsHandler.Current.GetSettings().SimplifiedGraphics = value;
                OverlaySettingsHandler.Current.Save();
            }
            OnPropertyChanged(nameof(SimplifiedGraphics));
        }
    }

    public bool DontRenderWhenPaused
    {
        get => OverlaySettingsHandler.Current.GetSettings().DontRenderWhenPaused;
        set
        {
            if (OverlaySettingsHandler.Current.GetSettings().DontRenderWhenPaused != value)
            {
                OverlaySettingsHandler.Current.GetSettings().DontRenderWhenPaused = value;
                OverlaySettingsHandler.Current.Save();
            }
            OnPropertyChanged(nameof(DontRenderWhenPaused));
        }
    }

    public int MaxARDistance
    {
        get => (int)OverlaySettingsHandler.Current.GetSettings().MaxARDistance;
        set
        {
            if (OverlaySettingsHandler.Current.GetSettings().MaxARDistance != (int)value)
            {
                OverlaySettingsHandler.Current.GetSettings().MaxARDistance = (int)value;
                OverlaySettingsHandler.Current.Save();
            }
            OnPropertyChanged(nameof(MaxARDistance));
            OnPropertyChanged(nameof(ARDistanceDisplay));
        }
    }

    public string ARDistanceDisplay => $"{UnitConversions.FromScientificUnits(
                                            UnitType.Distance, 
                                            OverlaySettingsHandler.Current.GetSettings().MaxARDistance, 
                                            StateSettingsHandler.Current.GetSettings().DisplayUnits
                                        ):F0} {UnitConversions.GetUnitAbbreviation(
                                            UnitType.Distance, 
                                            StateSettingsHandler.Current.GetSettings().DisplayUnits
                                        )}";

    public DisplaySettings()
    {
        InitializeComponent();
        LoadDisplayUnitOptions();

        AvaloniaXamlLoader.Load(this);
        DataContext = this;
    }

    private void LoadDisplayUnitOptions()
    {
        SelectedDisplayUnitOption = (int)StateSettingsHandler.Current.GetSettings().DisplayUnits;
        foreach (Units option in Enum.GetValues(typeof(Units)))
        {
            DisplayUnitOptions.Add(new TabStripItemHandler(option.ToString()));
        }
    }

    private void OnDisplayUnitChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedDisplayUnitOption >= 0 && SelectedDisplayUnitOption < DisplayUnitOptions.Count)
        {
            StateSettingsHandler.Current.GetSettings().DisplayUnits = (Units)SelectedDisplayUnitOption;
            StateSettingsHandler.Current.Save();
            OnPropertyChanged(nameof(ARDistanceDisplay));
            OnPropertyChanged(nameof(IsUsingScientificUnits));
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
