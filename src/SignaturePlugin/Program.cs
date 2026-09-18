using Ocrx.Sdk;
using Ocrx.Sdk.Overlay;
using SignaturePlugin;

// The whole lifecycle — connect, subscribe, feed ticks, reconnect, summarise — is the host's; this
// process only supplies the plugin. The host loads config.json itself (PluginHostOptions.ConfigFileName
// defaults to "config.json") for the two settings every plugin has: pipeName and saveDebugFrames.
var table = SignaturePlugin.SignatureTable.LoadUserFile();
var configPath = UserConfig.Ensure();
var config = PluginConfig.Load<SignaturePluginConfig>(configPath);

// The plugin keeps the pristine base config so a live theme switch always rebuilds the overlay from
// a clean preset; the host gets a themed copy so the startup overlay already matches the saved theme.
// Theming a clone rather than `config` itself is what keeps the derived dimensions out of the base.
var themedStartup = config.CloneForSettings<SignaturePluginConfig>(configPath);
OverlayThemes.Apply(themedStartup);
OverlayPositions.Apply(themedStartup);

// The overlay sink lives in the opt-in Ocrx.Sdk.Overlay package, so the core SDK cannot
// construct it: an "overlay" output whose factory was never registered here silently routes to a
// no-op sink. Registering it is what makes the config.json entry actually draw. The factory itself
// degrades to a no-op off Windows, so this stays safe on any platform.
var options = new PluginHostOptions { Config = themedStartup, OverlayFactory = new OverlaySinkFactory() };

return await OcrxPluginHost.RunAsync(new SignaturePlugin.SignaturePlugin(table, config, configPath), args, options);
