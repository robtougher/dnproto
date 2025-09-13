using System.Reflection;
using dnproto.cli.commands;
using dnproto.log;
using dnproto.ws;

namespace dnproto.cli;

public static class CommandLineInterface
{
    /// <summary>
    /// Runs console program.
    /// </summary>
    /// <param name="arguments"></param>
    /// <exception cref="Exception"></exception>
    public static void RunMain(string[] args)
    {
        //
        // Parse args
        //
        var arguments = ParseArguments(args);


        //
        // Create logger
        //
        var logger = new ConsoleLogger();


        //
        // Check if they want trace logging
        //
        if (arguments.ContainsKey("loglevel") && GetArgumentValue(arguments, "loglevel") == "trace")
        {
            logger.LogLevel = 0; // trace
        }
        else if (arguments.ContainsKey("loglevel") && GetArgumentValue(arguments, "loglevel") == "info")
        {
            logger.LogLevel = 1; // info
        }
        else if (arguments.ContainsKey("loglevel") && GetArgumentValue(arguments, "loglevel") == "warning")
        {
            logger.LogLevel = 2; // warning
        }

        BlueskyClient.Logger = logger;


        //
        // Log parsed arguments
        //
        logger.LogTrace("Parsed arguments length: " + arguments.Keys.Count);
        if(arguments.Keys.Count > 0)
        {
            logger.LogTrace("Parsed arguments:");
            foreach (var kvp in arguments)
            {
                if(kvp.Key == "password")
                {
                    logger.LogTrace($"    {kvp.Key}: ********");
                }
                else
                {
                    logger.LogTrace($"    {kvp.Key}: {kvp.Value}");
                }
            }
        }

        if (arguments.ContainsKey("command") == false)
        {
            arguments["command"] = "Help";
        }

        string commandName = arguments["command"];


        //
        // Do we want to debug?
        //
        if (arguments.ContainsKey("debugattach") && GetArgumentValue(arguments, "debugattach") == "true")
        {
            logger.LogLevel = 0; // trace

            logger.LogTrace("Waiting for debugger to attach.");

            while (!System.Diagnostics.Debugger.IsAttached)
            {
                System.Threading.Thread.Sleep(1000);
            }
        }



        //
        // Create command instance
        //
        BaseCommand? commandInstance = CommandLineInterface.TryCreateCommandInstance(commandName);

        if (commandInstance == null)
        {
            logger.LogError($"Command not found: {commandName}");

            commandInstance = CommandLineInterface.TryCreateCommandInstance("help");

            if(commandInstance == null)
            {
                throw new Exception("Help command not found.");
            }
            else
            {
                commandInstance.DoCommand(arguments);
                return;
            }
        }

        commandInstance.Logger = logger;

        //
        // Check that arguments exist. If not, print arguments and return.
        //
        if(CommandLineInterface.CheckArguments(commandInstance, arguments) == false)
        {
            CommandLineInterface.PrintUsage(commandName, commandInstance, logger);
            return;
        }


        // Do command
        logger.LogTrace($"Running command.");
        commandInstance.DoCommand(arguments);

    }


    /// <summary>
    /// Parses command line arguments into a dictionary.
    /// </summary>
    /// <param name="args">Array of command line arguments.</param>
    /// <returns>A dictionary where the key is the argument name and the value is the argument value.</returns>
    public static Dictionary<string, string> ParseArguments(string[] args)
    {
        if(args == null)
        {
            throw new Exception("Arguments cannot be null.");
        }
        
        string formatError = "Command line arguments must be in the format '/name1 value1 /name2 value2'";

        // Check that there are an even number of arguments
        if (args.Length % 2 != 0)
        {
            throw new Exception(formatError);
        }

        // Loop and turn them into key/value pairs
        var arguments = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 < args.Length)
            {
                if (args[i].StartsWith("/"))
                {
                    arguments[args[i].Substring(1).ToLower()] = args[i + 1];
                }
                else
                {
                    throw new Exception(formatError);
                }
            }
            else
            {
                throw new Exception(formatError);
            }
        }
        
        return arguments;
    }




    /// <summary>
    /// Tries to create an instance of a command by its name.
    /// </summary>
    /// <param name="commandName"></param>
    /// <returns></returns>
    public static dnproto.cli.commands.BaseCommand? TryCreateCommandInstance(string commandName)
    {
        var commandType = TryFindCommandType(commandName);
        return (commandType is not null ? Activator.CreateInstance(commandType) as dnproto.cli.commands.BaseCommand : null);
    }

    /// <summary>
    /// Finds a class in the given assembly by its namespace and case-insensitive name.
    /// </summary>
    /// <param name="assembly">The assembly to search.</param>
    /// <param name="className">The case-insensitive name of the class to find.</param>
    /// <returns>The Type of the class if found, otherwise null.</returns>
    public static Type? TryFindCommandType(string className)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        foreach (Type type in assembly.GetTypes())
        {
            if (type.Namespace == "dnproto.cli.commands" && string.Equals(type.Name, className, StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        return null;
    }

    public static List<Type> GetAllCommandTypes()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        List<Type> commands = new List<Type>();

        foreach (Type type in assembly.GetTypes())
        {
            if (type.Namespace == "dnproto.cli.commands" && typeof(BaseCommand).IsAssignableFrom(type) && type.Name != "BaseCommand")
            {
                commands.Add(type);
            }
        }

        return commands;
    }



    public static bool CheckArguments(dnproto.cli.commands.BaseCommand command, Dictionary<string, string> userArguments)
    {
        var requiredArguments = command.GetRequiredArguments().Select(arg => arg.ToLower()).ToList();
        var optionalArguments = command.GetOptionalArguments().Select(arg => arg.ToLower()).ToList();
        var reservedArguments = GetReservedArguments();

        // Check for missing required arguments
        foreach (var requiredArgument in requiredArguments)
        {
            if (userArguments.ContainsKey(requiredArgument) == false)
            {
                return false;
            }
        }

        // Check for required arguments that clash with reserved arguments
        foreach (var requiredArgument in requiredArguments)
        {
            if (reservedArguments.Contains(requiredArgument))
            {
                return false;
            }
        }

        // Check for unknown arguments
        foreach (var userArgument in userArguments)
        {
            if (requiredArguments.Contains(userArgument.Key) == false 
                && optionalArguments.Contains(userArgument.Key) == false 
                && reservedArguments.Contains(userArgument.Key) == false)
            {
                return false;
            }
        }

        // Check for optional arguments that clash with reserved arguments
        foreach (var optionalArgument in optionalArguments)
        {
            if (reservedArguments.Contains(optionalArgument))
            {
                return false;
            }
        }

        return true;
    }

    public static void PrintUsage(string commandName, dnproto.cli.commands.BaseCommand commandInstance, BaseLogger logger)
    {
        logger.LogInfo("Usage:");
        string usage = "    .\\dnproto.exe /command " + commandName + "";

        // Required arguments
        foreach (var requiredArgument in commandInstance.GetRequiredArguments())
        {
            usage += " /" + requiredArgument + " val ";
        }

        // Optional arguments
        foreach (var optionalArgument in commandInstance.GetOptionalArguments())
        {
            usage += " [/" + optionalArgument + " val] ";
        }

        logger.LogInfo(usage);
    }

    public static HashSet<string> GetReservedArguments()
    {
        return new HashSet<string>(new string[] { "command", "debugattach", "loglevel" });
    }

    public static string? GetArgumentValue(Dictionary<string, string> arguments, string argumentName)
    {
        if(arguments.ContainsKey(argumentName.ToLower()) == false)
        {
            return null;
        }

        return arguments[argumentName.ToLower()];
    }

    public static int GetArgumentValueWithDefault(Dictionary<string, string> arguments, string argumentName, int defaultValue)
    {
        if(arguments.ContainsKey(argumentName.ToLower()) == false)
        {
            return defaultValue;
        }

        int val = 0;

        if(int.TryParse(arguments[argumentName.ToLower()], out val))
        {
            return val;
        }

        return defaultValue;
    }

    public static bool GetArgumentValueWithDefault(Dictionary<string, string> arguments, string argumentName, bool defaultValue)
    {
        if(arguments.ContainsKey(argumentName.ToLower()) == false)
        {
            return defaultValue;
        }

        bool val;

        if(bool.TryParse(arguments[argumentName.ToLower()], out val))
        {
            return val;
        }

        return defaultValue;
    }

    public static string GetArgumentValueWithDefault(Dictionary<string, string> arguments, string argumentName, string defaultValue)
    {
        return arguments.ContainsKey(argumentName.ToLower()) ? arguments[argumentName.ToLower()] : defaultValue;
    }


    public static bool HasArgument(Dictionary<string, string> arguments, string argumentName)
    {
        return arguments != null && arguments.ContainsKey(argumentName.ToLower());
    }


}