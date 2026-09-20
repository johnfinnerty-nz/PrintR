namespace PrintR.Agent;

public static class PrintRequestValidation
{
    public static bool IsPageRangeValid(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return true;
        if (range.Length > 200) return false;
        return range.Split(',').All(part =>
        {
            var pages = part.Trim().Split('-');
            return pages.Length is 1 or 2 && pages.All(p => int.TryParse(p.Trim(), out var n) && n is > 0 and <= 100000) &&
                (pages.Length == 1 || int.Parse(pages[0]) <= int.Parse(pages[1]));
        });
    }
}
