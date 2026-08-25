using LunchOrganizer.Domain.Security;

const string usage = """
    LunchOrganizer.AdminHash — hashes a password into the pbkdf2-sha256 token format
    used for the "Password" field of entries in admin-users.json.

    Usage:
      LunchOrganizer.AdminHash [password]
      LunchOrganizer.AdminHash --help

    If no password argument is given, you will be prompted interactively with masked
    ("*") input instead.

    Options:
      --help, -h, -?   Show this help text.

    Exit codes:
      0  Token printed successfully
      1  Error (empty password, too many arguments, or hashing failure)
    """;

if (args.Length > 0 && IsHelpFlag(args[0]))
{
    Console.WriteLine(usage);
    return 0;
}

if (args.Length > 1)
{
    Console.Error.WriteLine("Error: too many arguments. Expected at most one positional argument (the password).");
    Console.Error.WriteLine(usage);
    return 1;
}

string? password = args.Length == 1 ? args[0] : ReadPasswordInteractively();

if (string.IsNullOrEmpty(password))
{
    Console.Error.WriteLine("Password must not be empty.");
    return 1;
}

try
{
    var token = AdminPasswordHasher.Hash(password);
    Console.WriteLine("Paste the following token as the \"Password\" value for this user in admin-users.json:");
    Console.WriteLine(token);
    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static bool IsHelpFlag(string arg) =>
    string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(arg, "-?", StringComparison.OrdinalIgnoreCase);

static string? ReadPasswordInteractively()
{
    if (Console.IsInputRedirected)
    {
        // No masking is possible when input is redirected (e.g. piped from a file or another
        // process) — just read the line as-is.
        return Console.ReadLine();
    }

    Console.Write("Enter password: ");

    var buffer = new System.Text.StringBuilder();
    while (true)
    {
        var keyInfo = Console.ReadKey(intercept: true);

        if (keyInfo.Key == ConsoleKey.Enter)
        {
            break;
        }

        if (keyInfo.Key == ConsoleKey.Backspace)
        {
            if (buffer.Length > 0)
            {
                buffer.Remove(buffer.Length - 1, 1);
                Console.Write("\b \b");
            }

            continue;
        }

        if (keyInfo.KeyChar != '\0')
        {
            buffer.Append(keyInfo.KeyChar);
            Console.Write('*');
        }
    }

    Console.WriteLine();
    return buffer.ToString();
}
