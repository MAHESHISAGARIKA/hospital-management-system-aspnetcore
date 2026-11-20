namespace HospitalManagementSystem.Web.Services;

public static class ConsoleSecretReader
{
    public static string Read(string prompt)
    {
        Console.Write(prompt);
        var value = string.Empty;

        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && value.Length > 0)
            {
                value = value[..^1];
            }
            else if (!char.IsControl(key.KeyChar))
            {
                value += key.KeyChar;
            }
        }

        Console.WriteLine();
        return value;
    }
}
