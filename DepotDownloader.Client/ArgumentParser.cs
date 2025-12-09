using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace DepotDownloader.Client;

/// <summary>
///     Parses command-line arguments with consumed argument tracking.
/// </summary>
public sealed class ArgumentParser(string[] args)
{
    private readonly bool[] _consumed = new bool[args.Length];

    public bool HasUnconsumedArgs()
    {
        return _consumed.Any(c => !c);
    }

    public IEnumerable<string> GetUnconsumedArgs()
    {
        for (var i = 0; i < args.Length; i++)
            if (!_consumed[i])
                yield return $"Argument #{i + 1} {args[i]}";
    }

    public bool Has(params string[] parameters)
    {
        return IndexOf(parameters) > -1;
    }

    public T Get<T>(T defaultValue, params string[] parameters)
    {
        var index = IndexOf(parameters);
        if (index == -1 || index == args.Length - 1)
            return defaultValue;

        var value = args[index + 1];
        var converter = TypeDescriptor.GetConverter(typeof(T));
        _consumed[index + 1] = true;
        return (T)converter.ConvertFromString(value);
    }

    public List<T> GetList<T>(params string[] parameters)
    {
        var list = new List<T>();
        var index = IndexOf(parameters);

        if (index == -1 || index == args.Length - 1)
            return list;

        index++;
        while (index < args.Length && args[index][0] != '-')
        {
            var converter = TypeDescriptor.GetConverter(typeof(T));
            _consumed[index] = true;
            list.Add((T)converter.ConvertFromString(args[index]));
            index++;
        }

        return list;
    }

    private int IndexOf(params string[] parameters)
    {
        for (var i = 0; i < args.Length; i++)
            if (parameters.Any(p => args[i].Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                _consumed[i] = true;
                return i;
            }

        return -1;
    }
}