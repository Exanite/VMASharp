using System;
using VMASharp;

namespace VulkanCube;

public class Application
{
    private static void Main(string[] args)
    {
        try
        {
            var app = new DrawCubeExample();

            app.Run();

            app.Dispose();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);

            if (e is VulkanResultException ve)
            {
                Console.WriteLine("\nResult Code: " + ve.Result);
            }
        }
    }
}
