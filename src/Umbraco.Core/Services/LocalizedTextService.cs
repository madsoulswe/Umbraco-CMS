using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Extensions.Logging;

namespace Umbraco.Cms.Core.Services;

/// <inheritdoc />
public class LocalizedTextService : ILocalizedTextService
{
    private readonly Lazy<IDictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>>> _dictionarySourceLazy;
    private readonly Lazy<LocalizedTextServiceFileSources>? _fileSources;
    private readonly ILogger<LocalizedTextService> _logger;
    private readonly Lazy<IDictionary<CultureInfo, Lazy<IDictionary<string, string>>>> _noAreaDictionarySourceLazy;

    private IDictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>> DictionarySource => _dictionarySourceLazy.Value;
    private IDictionary<CultureInfo, Lazy<IDictionary<string, string>>> NoAreaDictionarySource => _noAreaDictionarySourceLazy.Value;


    /// <summary>
    ///     Initializes with a file sources instance
    /// </summary>
    /// <param name="fileSources"></param>
    /// <param name="logger"></param>
    public LocalizedTextService(Lazy<LocalizedTextServiceFileSources> fileSources, ILogger<LocalizedTextService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fileSources);

        _logger = logger;
        _fileSources = fileSources;

        _dictionarySourceLazy = CreateDictionarySourceLazy(() => FileSourcesToAreaDictionarySources(fileSources.Value));
        _noAreaDictionarySourceLazy = CreateNoAreaDictionarySourceLazy(() => FileSourcesToNoAreaDictionarySources(fileSources.Value));
    }

    /// <summary>
    ///     Initializes with an XML source
    /// </summary>
    /// <param name="source"></param>
    /// <param name="logger"></param>
    public LocalizedTextService(IDictionary<CultureInfo, Lazy<XDocument>> source, ILogger<LocalizedTextService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _dictionarySourceLazy = CreateDictionarySourceLazy(() => XmlSourcesToAreaDictionary(source));
        _noAreaDictionarySourceLazy = CreateNoAreaDictionarySourceLazy(() => XmlSourceToNoAreaDictionary(source));
    }

    /// <summary>
    /// Initializes with a source of a dictionary of culture -> areas -> sub dictionary of keys/values
    /// </summary>
    /// <param name="source"></param>
    /// <param name="logger"></param>
    public LocalizedTextService(IDictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>> source, ILogger<LocalizedTextService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _dictionarySourceLazy = CreateDictionarySourceLazy(() => source);
        _noAreaDictionarySourceLazy = CreateNoAreaDictionarySourceLazy(() => CreateNoAreaDictionary(source));
    }

    private Lazy<IDictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>>> CreateDictionarySourceLazy(Func<IDictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>>> factory)
    {
        return new Lazy<IDictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>>>(factory);
    }

    private Lazy<IDictionary<CultureInfo, Lazy<IDictionary<string, string>>>> CreateNoAreaDictionarySourceLazy(Func<IDictionary<CultureInfo, Lazy<IDictionary<string, string>>>> factory)
    {
        return new Lazy<IDictionary<CultureInfo, Lazy<IDictionary<string, string>>>>(factory);
    }

    private IDictionary<CultureInfo, Lazy<IDictionary<string, string>>> CreateNoAreaDictionary(IDictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>> source)
    {
        var cultureNoAreaDictionary = new Dictionary<CultureInfo, Lazy<IDictionary<string, string>>>();

        foreach (KeyValuePair<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>> cultureDictionary in source)
        {
            Dictionary<string, IDictionary<string, string>> areaAliaValue = GetAreaStoredTranslations(source, cultureDictionary.Key);
            cultureNoAreaDictionary[cultureDictionary.Key] = new Lazy<IDictionary<string, string>>(() => GetAliasValues(areaAliaValue));
        }

        return cultureNoAreaDictionary;
    }

    /// <summary>
    /// Localizes a key with the specified culture in format of {area}/{alias}
    /// </summary>
    public string Localize(string key, CultureInfo culture, IDictionary<string, string?>? tokens = null)
    {
        ArgumentNullException.ThrowIfNull(culture);

        // This is what the legacy ui service did
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var keyParts = key.Split(Constants.CharArrays.ForwardSlash, StringSplitOptions.RemoveEmptyEntries);
        var area = keyParts.Length > 1 ? keyParts[0] : null;
        var alias = keyParts.Length > 1 ? keyParts[1] : keyParts[0];
        return Localize(area, alias, culture, tokens);
    }

    /// <summary>
    /// Localizes a key with the specified culture
    /// </summary>
    public string Localize(string? area, string? alias, CultureInfo? culture, IDictionary<string, string?>? tokens = null)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (string.IsNullOrEmpty(alias))
        {
            return string.Empty;
        }

        // TODO: Hack, see notes on ConvertToSupportedCultureWithRegionCode
        culture = ConvertToSupportedCultureWithRegionCode(culture);

        return GetFromDictionarySource(culture, area, alias, tokens);
    }

    /// <summary>
    ///     Returns all key/values in storage for the given culture
    /// </summary>
    public IDictionary<string, string> GetAllStoredValues(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        // TODO: Hack, see notes on ConvertToSupportedCultureWithRegionCode
        culture = ConvertToSupportedCultureWithRegionCode(culture);

        if (!DictionarySource.TryGetValue(culture, out Lazy<IDictionary<string, IDictionary<string, string>>>? cultureDictionary))
        {
            LogWarning($"The culture specified {culture} was not found in any configured sources for this service");
            return ReadOnlyDictionary<string, string>.Empty;
        }

        var result = new Dictionary<string, string>();

        // convert all areas + keys to a single key with a '/'

        foreach (KeyValuePair<string, IDictionary<string, string>> area in cultureDictionary.Value)
        {
            foreach (KeyValuePair<string, string> key in area.Value)
            {
                // i don't think it's possible to have duplicates because we're dealing with a dictionary in the first place, but we'll double check here just in case.
                _ = result.TryAdd($"{area.Key}/{key.Key}", key.Value);
            }
        }

        return result;
    }


    /// <summary>
    ///     Returns all key/values in storage for the given culture
    /// </summary>
    /// <returns></returns>
    public IDictionary<string, IDictionary<string, string>> GetAllStoredValuesByAreaAndAlias(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        // TODO: Hack, see notes on ConvertToSupportedCultureWithRegionCode
        culture = ConvertToSupportedCultureWithRegionCode(culture);

        if (!DictionarySource.TryGetValue(culture, out Lazy<IDictionary<string, IDictionary<string, string>>>? cultureDictionary))
        {
            LogWarning($"The culture specified {culture} was not found in any configured sources for this service");
            return ReadOnlyDictionary<string, IDictionary<string, string>>.Empty;
        }

        return cultureDictionary.Value;
    }

    /// <summary>
    ///     Returns a list of all currently supported cultures
    /// </summary>
    /// <returns></returns>
    public IEnumerable<CultureInfo> GetSupportedCultures() => DictionarySource.Keys;

    /// <summary>
    ///     Tries to resolve a full 4 letter culture from a 2 letter culture name
    /// </summary>
    /// <param name="currentCulture">
    ///     The culture to determine if it is only a 2 letter culture, if so we'll try to convert it, otherwise it will just be
    ///     returned
    /// </param>
    /// <returns></returns>
    /// <remarks>
    ///     TODO: This is just a hack due to the way we store the language files, they should be stored with 4 letters since
    ///     that
    ///     is what they reference but they are stored with 2, further more our user's languages are stored with 2. So this
    ///     attempts
    ///     to resolve the full culture if possible.
    ///     This only works when this service is constructed with the LocalizedTextServiceFileSources
    /// </remarks>
    public CultureInfo ConvertToSupportedCultureWithRegionCode(CultureInfo currentCulture)
    {
        ArgumentNullException.ThrowIfNull(currentCulture);

        if (_fileSources == null || currentCulture.Name.Length > 2)
        {
            return currentCulture;
        }

        Attempt<CultureInfo?> attempt = _fileSources.Value.TryConvert2LetterCultureTo4Letter(currentCulture.TwoLetterISOLanguageName);
        return attempt.Success ? attempt.Result! : currentCulture;
    }


    

    /// <summary>
    ///     Parses the tokens in the value
    /// </summary>
    /// <param name="value"></param>
    /// <param name="tokens"></param>
    /// <returns></returns>
    /// <remarks>
    ///     This is based on how the legacy ui localized text worked, each token was just a sequential value delimited with a %
    ///     symbol.
    ///     For example: hello %0%, you are %1% !
    ///     Since we're going to continue using the same language files for now, the token system needs to remain the same.
    ///     With our new service
    ///     we support a dictionary which means in the future we can really have any sort of token system.
    ///     Currently though, the token key's will need to be an integer and sequential - though we aren't going to throw
    ///     exceptions if that is not the case.
    /// </remarks>
    internal static string ParseTokens(string value, IDictionary<string, string?>? tokens)
    {
        if (tokens == null || !tokens.Any())
        {
            return value;
        }

        foreach (KeyValuePair<string, string?> token in tokens)
        {
            value = value.Replace($"%{token.Key}%", token.Value);
        }

        return value;
    }

    private static Dictionary<string, string> GetAliasValues(Dictionary<string, IDictionary<string, string>> areaAliaValue)
    {
        var aliasValue = new Dictionary<string, string>();
        foreach (KeyValuePair<string, IDictionary<string, string>> area in areaAliaValue)
        {
            foreach (KeyValuePair<string, string> alias in area.Value)
            {
                _ = aliasValue.TryAdd(alias.Key, alias.Value);
            }
        }

        return aliasValue;
    }

    private IDictionary<CultureInfo, Lazy<IDictionary<string, string>>> FileSourcesToNoAreaDictionarySources(LocalizedTextServiceFileSources fileSources)
    {
        IDictionary<CultureInfo, Lazy<XDocument>> xmlSources = fileSources.GetXmlSources();
        return XmlSourceToNoAreaDictionary(xmlSources);
    }

    private IDictionary<CultureInfo, Lazy<IDictionary<string, string>>> XmlSourceToNoAreaDictionary(IDictionary<CultureInfo, Lazy<XDocument>> xmlSources)
    {
        var cultureNoAreaDictionary = new Dictionary<CultureInfo, Lazy<IDictionary<string, string>>>();

        foreach (KeyValuePair<CultureInfo, Lazy<XDocument>> xmlSource in xmlSources)
        {
            var noAreaAliasValue = new Lazy<IDictionary<string, string>>(() => GetNoAreaStoredTranslations(xmlSources, xmlSource.Key));
            cultureNoAreaDictionary[xmlSource.Key] = noAreaAliasValue;
        }

        return cultureNoAreaDictionary;
    }

    private IDictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>> FileSourcesToAreaDictionarySources(LocalizedTextServiceFileSources fileSources)
    {
        IDictionary<CultureInfo, Lazy<XDocument>> xmlSources = fileSources.GetXmlSources();
        return XmlSourcesToAreaDictionary(xmlSources);
    }

    private IDictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>> XmlSourcesToAreaDictionary(IDictionary<CultureInfo, Lazy<XDocument>> xmlSources)
    {
        var cultureDictionary = new Dictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>>();

        foreach (KeyValuePair<CultureInfo, Lazy<XDocument>> xmlSource in xmlSources)
        {
            var areaAliaValue = new Lazy<IDictionary<string, IDictionary<string, string>>>(() => GetAreaStoredTranslations(xmlSources, xmlSource.Key));
            cultureDictionary[xmlSource.Key] = areaAliaValue;
        }

        return cultureDictionary;
    }

    private IDictionary<string, IDictionary<string, string>> GetAreaStoredTranslations(IDictionary<CultureInfo, Lazy<XDocument>> xmlSource, CultureInfo cult)
    {
        var overallResult = new Dictionary<string, IDictionary<string, string>>(StringComparer.InvariantCulture);
        IEnumerable<XElement> areas = xmlSource[cult].Value.XPathSelectElements("//area");

        foreach (XElement area in areas)
        {
            var areaAlias = area.GetAlias();
            if (!overallResult.TryGetValue(areaAlias, out IDictionary<string, string>? result))
            {
                result = new Dictionary<string, string>(StringComparer.InvariantCulture);
            }

            IEnumerable<XElement> keys = area.XPathSelectElements("./key");
            foreach (XElement key in keys)
            {
                // there could be duplicates if the language file isn't formatted nicely - which is probably the case for quite a few lang files
                _ = result.TryAdd(key.GetAlias(), key.Value);
            }

            _ = overallResult.TryAdd(areaAlias, result);
        }

        var englishCulture = new CultureInfo("en-US");
        if (!cult.Equals(englishCulture))
        {
            IEnumerable<XElement> enUS = xmlSource[englishCulture].Value.XPathSelectElements("//area");
            foreach (XElement area in enUS)
            {
                var areaAlias = area.GetAlias();
                if (!overallResult.TryGetValue(areaAlias, out IDictionary<string, string>? result))
                {
                    result = new Dictionary<string, string>(StringComparer.InvariantCulture);
                }

                IEnumerable<XElement> keys = area.XPathSelectElements("./key");

                foreach (XElement key in keys)
                {
                    _ = result.TryAdd(key.GetAlias(), key.Value);
                }

                _ = overallResult.TryAdd(areaAlias, result);
            }
        }

        return overallResult;
    }

    private Dictionary<string, string> GetNoAreaStoredTranslations(IDictionary<CultureInfo, Lazy<XDocument>> xmlSource, CultureInfo cult)
    {
        var result = new Dictionary<string, string>(StringComparer.InvariantCulture);
        IEnumerable<XElement> keys = xmlSource[cult].Value.XPathSelectElements("//key");

        foreach (XElement key in keys)
        {
            _ = result.TryAdd(key.GetAlias(), key.Value);
        }

        var englishCulture = new CultureInfo("en-US");
        if (!cult.Equals(englishCulture))
        {
            IEnumerable<XElement> keysEn = xmlSource[englishCulture].Value.XPathSelectElements("//key");

            foreach (XElement key in keysEn)
            {
                _ = result.TryAdd(key.GetAlias(), key.Value);
            }
        }

        return result;
    }

    private Dictionary<string, IDictionary<string, string>> GetAreaStoredTranslations(IDictionary<CultureInfo, Lazy<IDictionary<string, IDictionary<string, string>>>> dictionarySource, CultureInfo cult)
    {
        var overallResult = new Dictionary<string, IDictionary<string, string>>(StringComparer.InvariantCulture);
        Lazy<IDictionary<string, IDictionary<string, string>>> areaDict = dictionarySource[cult];

        foreach (KeyValuePair<string, IDictionary<string, string>> area in areaDict.Value)
        {
            var result = new Dictionary<string, string>(StringComparer.InvariantCulture);

            foreach (var key in area.Value.Keys)
            {
                _ = result.TryAdd(key, area.Value[key]);
            }

            overallResult[area.Key] = result;
        }

        return overallResult;
    }

    private string GetFromDictionarySource(CultureInfo culture, string? area, string key, IDictionary<string, string?>? tokens)
    {
        if (!DictionarySource.TryGetValue(culture, out Lazy<IDictionary<string, IDictionary<string, string>>>? cultureDictionary))
        {
            LogWarning($"The culture specified {culture} was not found in any configured sources for this service");
            return $"[{key}]";
        }

        string? found = null;
        if (string.IsNullOrWhiteSpace(area))
        {
            _ = NoAreaDictionarySource[culture].Value.TryGetValue(key, out found);
        }
        else
        {
            if (cultureDictionary.Value.TryGetValue(area, out IDictionary<string, string>? areaDictionary))
            {
                _ = areaDictionary.TryGetValue(key, out found);
            }

            if (found == null)
            {
                _ = NoAreaDictionarySource[culture].Value.TryGetValue(key, out found);
            }
        }

        return found != null ? ParseTokens(found, tokens) : $"[{key}]";
    }

    private static string GetAlias(XElement element)
        => element.GetAlias();

    private static string GetRequiredAttributeValue(XElement element, string attributeName)
        => element.Attribute(attributeName)?.Value ?? throw new InvalidOperationException($"The '{attributeName}' attribute is missing.");

    private void LogWarning(string message)
        => _logger.LogWarning(message);

    
}

static class Extensions {
    public static string GetAlias(this XElement element)
        => GetRequiredAttributeValue(element, "alias");

    public static string GetRequiredAttributeValue(this XElement element, string attributeName)
        => element.Attribute(attributeName)?.Value ?? throw new InvalidOperationException($"The '{attributeName}' attribute is missing.");

}
