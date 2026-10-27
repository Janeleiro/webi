using System;
using Godot;

namespace Webi.UI;

/// <summary>
/// A small native dialog with one text field (new timer, sticky note, birthday). Enter or OK confirms;
/// Esc or Cancel closes. One instance is reused for every question.
/// </summary>
public partial class InputDialog : ConfirmationDialog
{
    readonly Label _prompt = new();
    readonly LineEdit _field = new();
    Action<string>? _done;

    public override void _Ready()
    {
        Title = "Webi";
        OkButtonText = "OK";
        CancelButtonText = "Cancelar";
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _prompt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _prompt.CustomMinimumSize = new Vector2(360, 0);
        box.AddChild(_prompt);
        _field.CustomMinimumSize = new Vector2(360, 0);
        box.AddChild(_field);
        AddChild(box);
        RegisterTextEnter(_field);
        Confirmed += () =>
        {
            var text = _field.Text.Trim();
            var done = _done;
            _done = null;
            if (text.Length > 0) done?.Invoke(text);
        };
        Canceled += () => _done = null;
    }

    /// <summary>Asks for a line of text; <paramref name="done"/> gets it (not called for empty text or cancel).</summary>
    public void Ask(string prompt, string placeholder, string initial, Action<string> done, int maxLength = 0)
    {
        _prompt.Text = prompt;
        _field.PlaceholderText = placeholder;
        _field.Text = initial;
        _field.MaxLength = maxLength;
        _done = done;
        PopupCentered();
        // Bring the dialog to the front with the keyboard in the field (Webi's own windows never take focus).
        GrabFocus();
        _field.CallDeferred(Control.MethodName.GrabFocus);
        _field.CallDeferred(LineEdit.MethodName.SelectAll);
    }
}
