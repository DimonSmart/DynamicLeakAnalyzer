class Program
{
    static void Main()
    {
        static dynamic GetDynamic() => 42;

        dynamic d = GetDynamic();
        int value = d; // DSM001
        var inferred = d; // DSM002

        Console.WriteLine($"value={value}, inferred={inferred}");
    }
}