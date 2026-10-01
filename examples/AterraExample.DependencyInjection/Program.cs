// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using AterraEngine.Core.DependencyInjection;

namespace AterraExample.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal class Program {
    public static async Task Main(string[] args) {
        var serviceCollection = new ServiceCollection();

        serviceCollection.RegisterServicesFromAssembly<Program>();

        ServiceProvider serviceProvider = serviceCollection.Build();

        SomeService someService = serviceProvider.Get<SomeService>();
        someService.DoSomething();
    }
}

[TransientService<SomeService>]
public class SomeService(IWriter output) {
    public void DoSomething() {
        output.Write("something");
    }
}

public interface IWriter {
    void Write(string input);
}

[SingletonService<IWriter>]
public class WriteOutput : IWriter {
    public void Write(string input) {
        Console.WriteLine(input);
    }
}
