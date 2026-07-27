using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WPF_OpenStreetmap_Editor.Services;

internal sealed class NonTextInputImeGuard : IDisposable {
    private readonly Window _window;
    private readonly KeyboardFocusChangedEventHandler _focusChangedHandler;
    private bool _disposed;

    private NonTextInputImeGuard(Window window) {
        _window = window;
        _focusChangedHandler = OnGotKeyboardFocus;
    }

    public static NonTextInputImeGuard Attach(Window window) {
        var guard = new NonTextInputImeGuard(window);
        window.AddHandler(Keyboard.GotKeyboardFocusEvent, guard._focusChangedHandler, true);
        window.Closed += guard.Window_Closed;
        guard.ApplyToFocusedElement(Keyboard.FocusedElement as DependencyObject ?? window);
        return guard;
    }

    public void Dispose() {
        if (_disposed) return;

        _window.RemoveHandler(Keyboard.GotKeyboardFocusEvent, _focusChangedHandler);
        _window.Closed -= Window_Closed;
        _disposed = true;
    }

    internal static bool IsEditableTextInput(DependencyObject? element) {
        for (var current = element; current is not null; current = GetParent(current)) {
            switch (current) {
                case TextBoxBase { IsEnabled: true, IsReadOnly: false }:
                    return true;
                case PasswordBox { IsEnabled: true }:
                    return true;
                case ComboBox { IsEnabled: true, IsEditable: true }:
                    return true;
            }
        }

        return false;
    }

    private void Window_Closed(object? sender, EventArgs e) {
        Dispose();
    }

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
        ApplyToFocusedElement(e.NewFocus as DependencyObject ?? _window);
    }

    private void ApplyToFocusedElement(DependencyObject focusedElement) {
        var allowIme = IsEditableTextInput(focusedElement);
        InputMethod.SetIsInputMethodEnabled(focusedElement, allowIme);
        InputMethod.SetPreferredImeState(
            focusedElement,
            allowIme ? InputMethodState.DoNotCare : InputMethodState.Off);
    }

    private static DependencyObject? GetParent(DependencyObject element) {
        if (element is FrameworkElement { Parent: { } frameworkParent }) return frameworkParent;
        if (element is FrameworkContentElement { Parent: { } contentParent }) return contentParent;

        var logicalParent = LogicalTreeHelper.GetParent(element);
        if (logicalParent is not null) return logicalParent;

        try {
            return VisualTreeHelper.GetParent(element);
        } catch (InvalidOperationException) {
            return null;
        }
    }
}
