using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.RegularExpressions;
using System.IO.Abstractions;
using InertiaCore.Models;
using Microsoft.Extensions.Options;

namespace InertiaCore.Utils;

public interface IViteBuilder
{
    HtmlString ReactRefresh();
    HtmlString Input(string path);
}

internal class ViteBuilder : IViteBuilder
{
    private IFileSystem _fileSystem;
    private readonly IOptions<ViteOptions> _options;

    public ViteBuilder(IOptions<ViteOptions> options) => (_fileSystem, _options) = (new FileSystem(), options);

    protected internal void UseFileSystem(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    //  Get the public directory and build path.
    private string GetPublicPathForFile(string path)
    {
        var pieces = new List<string> { _options.Value.PublicDirectory };
        if (!string.IsNullOrEmpty(_options.Value.BuildDirectory))
        {
            pieces.Add(_options.Value.BuildDirectory);
        }

        pieces.Add(path);
        return string.Join("/", pieces);
    }

    public HtmlString Input(string path)
    {
        if (IsRunningHot())
        {
            return new HtmlString(MakeModuleTag(Asset("@vite/client")) + MakeModuleTag(Asset(path)));
        }

        if (!_fileSystem.File.Exists(GetPublicPathForFile(_options.Value.ManifestFilename)))
        {
            throw new Exception("Vite Manifest is missing. Run `npm run build` and try again.");
        }

        var manifest = _fileSystem.File.ReadAllText(GetPublicPathForFile(_options.Value.ManifestFilename));
        var manifestJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(manifest);

        if (manifestJson == null)
        {
            throw new Exception("Vite Manifest is invalid. Run `npm run build` and try again.");
        }

        if (!manifestJson.ContainsKey(path))
        {
            throw new Exception("Asset not found in manifest: " + path);
        }

        var obj = manifestJson[path];
        var filePath = obj.GetProperty("file");

        if (IsCssPath(filePath.ToString()))
        {
            return new HtmlString(MakeTag(filePath.ToString()));
        }

        // Handle JS and CSS combo
        var html = MakeTag(filePath.ToString());

        try
        {
            var css = obj.GetProperty("css");
            html = css.EnumerateArray().Aggregate(html,
                (current, item) => current + MakeTag(item.ToString()));
        }
        catch (Exception)
        {
            // ignored
        }

        return new HtmlString(html);
    }

    private static string MakeModuleTag(string path)
    {
        var builder = new TagBuilder("script");
        builder.Attributes.Add("type", "module");
        builder.Attributes.Add("src", path);

        return new HtmlString(GetString(builder)).Value + "\n\t";
    }

    // Generate an appropriate tag for the given URL in HMR mode.
    private string MakeTag(string url)
    {
        return IsCssPath(url) ? MakeStylesheetTag(url) : MakeScriptTag(url);
    }

    // Generate a script tag for the given URL.
    private string MakeScriptTag(string filePath)
    {
        var builder = new TagBuilder("script");
        builder.Attributes.Add("type", "text/javascript");
        builder.Attributes.Add("src", Asset(filePath));
        return GetString(builder) + "\n\t";
    }

    // Generate a stylesheet tag for the given URL in HMR mode.
    private string MakeStylesheetTag(string filePath)
    {
        var builder = new TagBuilder("link");
        builder.Attributes.Add("rel", "stylesheet");
        builder.Attributes.Add("href", Asset(filePath));
        return GetString(builder).Replace("></link>", " />") + "\n\t";
    }

    // Determine whether the given path is a CSS file.
    private static bool IsCssPath(string path)
    {
        return Regex.IsMatch(path, @".\.(css|less|sass|scss|styl|stylus|pcss|postcss)", RegexOptions.IgnoreCase);
    }

    // Generate React refresh runtime script.
    public HtmlString ReactRefresh()
    {
        if (!IsRunningHot())
        {
            return new HtmlString("<!-- no hot -->");
        }

        var builder = new TagBuilder("script");
        builder.Attributes.Add("type", "module");

        var inner = $"import RefreshRuntime from '{Asset("@react-refresh")}';" +
                    "RefreshRuntime.injectIntoGlobalHook(window);" +
                    "window.$RefreshReg$ = () => { };" +
                    "window.$RefreshSig$ = () => (type) => type;" +
                    "window.__vite_plugin_react_preamble_installed__ = true;";

        builder.InnerHtml.AppendHtml(inner);

        return new HtmlString(GetString(builder));
    }

    // Get the path to a given asset when running in HMR mode.
    private string HotAsset(string path)
    {
        var hotFilePath = GetPublicPathForFile(_options.Value.HotFile);
        var hotContents = _fileSystem.File.ReadAllText(hotFilePath);

        return hotContents + "/" + path;
    }

    // Get the URL for an asset.
    private string Asset(string path)
    {
        if (IsRunningHot())
        {
            return HotAsset(path);
        }

        var pieces = new List<string>();
        if (!string.IsNullOrEmpty(_options.Value.BuildDirectory))
        {
            pieces.Add(_options.Value.BuildDirectory);
        }

        pieces.Add(path);
        return "/" + string.Join("/", pieces);
    }

    private bool IsRunningHot()
    {
        return _fileSystem.File.Exists(GetPublicPathForFile(_options.Value.HotFile));
    }

    private static string GetString(IHtmlContent content)
    {
        var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}

public static class Vite
{
    private static IViteBuilder _instance = default!;

    internal static void UseBuilder(IViteBuilder instance) => _instance = instance;

    // Generate tag(s) for the given input path.
    public static HtmlString Input(string path) => _instance.Input(path);

    // Generate React refresh runtime script.
    public static HtmlString ReactRefresh() => _instance.ReactRefresh();
}
