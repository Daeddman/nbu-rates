namespace NbuRates;

/// <summary>Вивід у консоль: кольорові повідомлення та таблиці.</summary>
public static class ConsoleUi
{
    public static void Title(string text)
    {
        var line = new string('═', text.Length + 4);
        Write($"╔{line}╗\n║  {text}  ║\n╚{line}╝", ConsoleColor.Cyan);
    }

    public static void Info(string message) => Write(message, ConsoleColor.DarkGray);
    public static void Success(string message) => Write("✓ " + message, ConsoleColor.Green);
    public static void Warn(string message) => Write("! " + message, ConsoleColor.Yellow);
    public static void Error(string message) => Write("✗ " + message, ConsoleColor.Red, Console.Error);

    /// <summary>Запит у користувача. Повертає null, якщо введення закрито (Ctrl+Z / Ctrl+D).</summary>
    public static string? Ask(string prompt)
    {
        Console.Write($"{prompt}: ");
        return Console.ReadLine()?.Trim();
    }

    /// <summary>Друкує таблицю з вирівнюванням стовпців. rightAligned — номери числових стовпців.</summary>
    public static void Table(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, params int[] rightAligned)
    {
        var widths = headers.Select(h => h.Length).ToArray();
        foreach (var row in rows)
            for (var i = 0; i < widths.Length && i < row.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);

        string Cell(string value, int i) =>
            rightAligned.Contains(i) ? value.PadLeft(widths[i]) : value.PadRight(widths[i]);

        string Line(char left, char mid, char right) =>
            left + string.Join(mid, widths.Select(w => new string('─', w + 2))) + right;

        Console.WriteLine(Line('┌', '┬', '┐'));
        Console.WriteLine("│ " + string.Join(" │ ", headers.Select(Cell)) + " │");
        Console.WriteLine(Line('├', '┼', '┤'));
        foreach (var row in rows)
            Console.WriteLine("│ " + string.Join(" │ ", widths.Select((_, i) => Cell(i < row.Length ? row[i] : "", i))) + " │");
        Console.WriteLine(Line('└', '┴', '┘'));
    }

    private static void Write(string message, ConsoleColor color, TextWriter? writer = null)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        (writer ?? Console.Out).WriteLine(message);
        Console.ForegroundColor = previous;
    }
}
