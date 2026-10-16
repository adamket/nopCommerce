# nopCommerce plugin content changes

Whenever you modify a plugin source `.cshtml`, `.js`, or `.css` file, copy the updated file to that plugin's nopCommerce output directory before finishing the task. Preserve the file's relative path within the plugin.

Resolve the output directory from the plugin project configuration and `plugin.json` SystemName. In this repository it is normally `src/Presentation/Nop.Web/Plugins/<SystemName>/`. For example, `src/Plugins/Apt.Nop.Plugin.Misc.AkeneoConnection/Views/AttributeMapping/AttributeMappings.cshtml` copies to `src/Presentation/Nop.Web/Plugins/Apt.Misc.AkeneoConnection/Views/AttributeMapping/AttributeMappings.cshtml`.

Create any missing destination subdirectories as needed, and verify that copied files match the source. Do this for every plugin content change, even when no plugin rebuild is needed or performed. If a required build also copies these files, verify the output after the build.
