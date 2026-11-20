namespace Semestrovaya.Server;

internal static class InputText
{
    public static bool HasInvalidCharacters(string value, bool multiline = false) =>
        value.Any(character => char.IsControl(character) &&
            !(multiline && character is '\r' or '\n' or '\t'));

    public static void Check(string value, string field, bool multiline = false)
    {
        if (HasInvalidCharacters(value, multiline))
            throw new RequestException(400, "В поле есть недопустимые управляющие символы.", field);
    }
}
