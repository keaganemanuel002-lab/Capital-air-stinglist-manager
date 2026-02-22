using System;
using Avalonia;
using Avalonia.Controls;

namespace StingListManager.Behaviors;

public sealed class UppercaseTextBoxBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<UppercaseTextBoxBehavior, TextBox, bool>("IsEnabled", true);

    private static readonly AttachedProperty<bool> IsUpdatingProperty =
        AvaloniaProperty.RegisterAttached<UppercaseTextBoxBehavior, TextBox, bool>("IsUpdating");

    static UppercaseTextBoxBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>(OnIsEnabledChanged);
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>(OnTextChanged);
    }

    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static bool GetIsUpdating(AvaloniaObject element) => element.GetValue(IsUpdatingProperty);

    private static void SetIsUpdating(AvaloniaObject element, bool value) => element.SetValue(IsUpdatingProperty, value);

    private static void OnIsEnabledChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
            ApplyUppercase(textBox);
    }

    private static void OnTextChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs args)
    {
        if (!GetIsEnabled(textBox))
            return;

        ApplyUppercase(textBox);
    }

    private static void ApplyUppercase(TextBox textBox)
    {
        if (GetIsUpdating(textBox))
            return;

        var value = textBox.Text;
        if (string.IsNullOrEmpty(value))
            return;

        var upper = value.ToUpperInvariant();
        if (string.Equals(value, upper, StringComparison.Ordinal))
            return;

        var caret = textBox.CaretIndex;

        SetIsUpdating(textBox, true);
        try
        {
            textBox.Text = upper;
            textBox.CaretIndex = Math.Clamp(caret, 0, upper.Length);
        }
        finally
        {
            SetIsUpdating(textBox, false);
        }
    }
}
