using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WPF_OpenStreetmap_Editor.Controls;

/// <summary>
/// A text box that lists suggestions in a popup while the user types. The popup never takes focus, so the
/// control also works as a DataGrid cell editor: keys that do not act on a highlighted suggestion are left
/// to the host (Enter commits, Escape cancels, Tab moves on).
/// </summary>
public class AutoCompleteTextBox : TextBox {
    private readonly ListBox _listBox;
    private readonly Border _popupBorder;
    private readonly Popup _popup;
    private bool _suggestOnNextTextChange;
    private bool _isAcceptingSuggestion;

    public AutoCompleteTextBox() {
        // Implicit styles only match the exact type, so opt into the application's TextBox style explicitly.
        SetResourceReference(StyleProperty, typeof(TextBox));
        _listBox = new ListBox {
            Focusable = false,
            BorderThickness = new Thickness(0),
            MaxHeight = 220
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);
        _listBox.SetResourceReference(BackgroundProperty, "Theme.SurfaceBrush");
        _listBox.SetResourceReference(ForegroundProperty, "Theme.TextBrush");
        _listBox.PreviewMouseLeftButtonDown += ListBox_PreviewMouseLeftButtonDown;

        _popupBorder = new Border {
            BorderThickness = new Thickness(1),
            Child = _listBox
        };
        _popupBorder.SetResourceReference(Border.BackgroundProperty, "Theme.SurfaceBrush");
        _popupBorder.SetResourceReference(Border.BorderBrushProperty, "Theme.BorderBrush");

        _popup = new Popup {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Focusable = false,
            Child = _popupBorder
        };
        Unloaded += (_, _) => ClosePopup();
    }

    /// <summary>Returns suggestions for the current text; null disables completion.</summary>
    public Func<string, IReadOnlyList<string>>? SuggestionProvider { get; set; }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e) {
        _suggestOnNextTextChange = true;
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        if (_popup.IsOpen) {
            switch (e.Key) {
                case Key.Down:
                    MoveHighlight(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    MoveHighlight(-1);
                    e.Handled = true;
                    return;
                case Key.Enter:
                case Key.Tab:
                    if (_listBox.SelectedItem is string suggestion) {
                        AcceptSuggestion(suggestion);
                        e.Handled = true;
                        return;
                    }
                    // Nothing highlighted: close the list and let the host commit or move focus as usual.
                    ClosePopup();
                    break;
                case Key.Escape:
                    ClosePopup();
                    e.Handled = true;
                    return;
            }
        } else if (e.Key == Key.Down && ShowSuggestions()) {
            // Down on a closed list opens it, like a combo box.
            e.Handled = true;
            return;
        }

        // Only user edits open the list; text set by bindings or the host stays quiet.
        if (e.Key is Key.Back or Key.Delete) _suggestOnNextTextChange = true;
        base.OnPreviewKeyDown(e);
    }

    protected override void OnTextChanged(TextChangedEventArgs e) {
        base.OnTextChanged(e);
        var suggest = _suggestOnNextTextChange;
        _suggestOnNextTextChange = false;
        if (suggest && !_isAcceptingSuggestion && IsKeyboardFocusWithin) ShowSuggestions();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) {
        base.OnLostKeyboardFocus(e);
        ClosePopup();
    }

    private bool ShowSuggestions() {
        var provider = SuggestionProvider;
        if (provider is null) {
            ClosePopup();
            return false;
        }

        var text = Text;
        var suggestions = provider(text);
        if (suggestions.Count == 0 || (suggestions.Count == 1 && suggestions[0] == text)) {
            ClosePopup();
            return false;
        }

        _listBox.ItemsSource = suggestions;
        _listBox.SelectedIndex = -1;
        _popupBorder.MinWidth = ActualWidth;
        _popup.IsOpen = true;
        return true;
    }

    private void ClosePopup() {
        _popup.IsOpen = false;
    }

    private void MoveHighlight(int delta) {
        var count = _listBox.Items.Count;
        if (count == 0) return;

        var index = _listBox.SelectedIndex + delta;
        if (index < 0) {
            index = count - 1;
        } else if (index >= count) {
            index = 0;
        }

        _listBox.SelectedIndex = index;
        _listBox.ScrollIntoView(_listBox.SelectedItem);
    }

    private void AcceptSuggestion(string suggestion) {
        _isAcceptingSuggestion = true;
        try {
            Text = suggestion;
            CaretIndex = suggestion.Length;
        } finally {
            _isAcceptingSuggestion = false;
        }
        ClosePopup();
    }

    private void ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // Accept on mouse down and swallow the click so the list item never takes keyboard focus away from the text box.
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(_listBox, source) is not ListBoxItem { Content: string suggestion }) {
            return;
        }

        AcceptSuggestion(suggestion);
        e.Handled = true;
    }
}
