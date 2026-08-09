using Avalonia.Controls;

namespace DiezPublishingStudio;

internal static class HumanAiPromptEditingSelfTest
{
    public static void Run()
    {
        var mustDo = LockedBox();
        var mustNotDo = LockedBox();
        var prompt = LockedBox();

        HumanAiPromptInputGuard.MakeEditable(mustDo);
        HumanAiPromptInputGuard.MakeEditable(mustNotDo);
        HumanAiPromptInputGuard.MakeEditable(prompt);

        RequireEditable(mustDo, "DEVE FARE");
        RequireEditable(mustNotDo, "NON DEVE FARE");
        RequireEditable(prompt, "PROMPT");

        prompt.Text = "Prompt generato";
        prompt.SelectAll();
        Require(prompt.CanCopy, "Il PROMPT selezionato non risulta copiabile.");
    }

    private static TextBox LockedBox() => new()
    {
        IsReadOnly = true,
        IsEnabled = false,
        IsHitTestVisible = false,
        Focusable = false,
        IsUndoEnabled = false,
        Text = "test"
    };

    private static void RequireEditable(TextBox box, string name)
    {
        Require(!box.IsReadOnly, $"{name} è ancora read-only.");
        Require(box.IsEnabled, $"{name} è disabilitato.");
        Require(box.IsHitTestVisible, $"{name} non riceve click.");
        Require(box.Focusable, $"{name} non riceve focus.");
        Require(box.IsUndoEnabled, $"Undo/Ctrl+Z non è attivo su {name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("AI PROMPT EDIT SELF-TEST: " + message);
    }
}
