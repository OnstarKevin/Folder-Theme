# Folder Theme Studio User Guide

Preview8 adds a configurable focus/schedule example, HH:mm reminders, a two-stage AI prompt and five bundled folder icons in the icon studio.

Previous build: `FolderThemeStudio-v0.1.0-beta.24-preview.7-setup.exe`. Open Plugins directly from the main sidebar; Settings uses large module cards and a back control. Plugin configuration remains available.




## Beta 24 Preview 3: appearance, metrics and media

Install `FolderThemeStudio-v0.1.0-beta.24-preview.3-setup.exe`. Enable CPU/RAM under Settings → Island; metrics stay visible on the right. Appearance contains image import, fade direction/profile/strength/start, lightening, opacity and the subtle flowing border. Recommended image: about1200×160px, under2MiB; import limit16MiB, automatically resized and privately copied. Music smoothly adjusts size/corners; hover for previous/play-pause/next, lyrics, timeline, source and memory details. Controls require compatible Windows media sessions. Actual player compatibility is pending user acceptance.

## Beta 24 modular settings preview

Install `FolderThemeStudio-v0.1.0-beta.24-preview.2-setup.exe`. Settings now live inside the main window, with direct sidebar access to General, Features, Island, Appearance, Lyrics, Messages, and Plugins. Module switches retain drafts; Save applies them in place. Discard reloads the last saved configuration, including drafts in other modules sharing that configuration. Plugin changes apply immediately, with inline permission consent. Tray and Island settings open the same workspace. File pickers remain for local files and folders.

## Beta 24 appearance preview

Install `FolderThemeStudio-v0.1.0-beta.24-preview.1-setup.exe`. Open the Island appearance section from the tray to import a local TTF/OTF/TTC (up to32MiB), choose a family, preview, save, or restore Microsoft YaHei. Private font copies survive source-file moves; no system font installation occurs. This preview restores the original image background and increases single-line height to36.4 DIP. The Beta23 transparent-default description below is superseded by this section. Operational acceptance is pending user testing.

## Install and start

1. Run `FolderThemeStudio-v0.1.0-beta.23-setup.exe`.
2. Stay online if the installer needs to install the x64 .NET 8 Desktop Runtime.
3. Home brings Desktop Island, Icon studio, Live folders, Automatic styles, and Backup & restore together. Tutorial and Settings are at the bottom of the sidebar.

## Desktop Island (Beta 23)

The Island uses a fully transparent background and native mouse pass-through. Hover for compact details, or disable hover in Settings. Choose light text for a dark wallpaper.

Use the tray to open Island settings/plugins or edit its position. Drag in edit mode; Esc, Finish editing, or 30 seconds restores pass-through. Opacity, animation, snapping and data modules have independent controls.

Plugins & DIY supports .islandpkg import/drop, an installable focus timer, independent enable/removal, a collapsed AI development prompt and SDK export. Data permissions require consent and the corresponding host module to be enabled. This release does not expose network, system commands or DLL loading.

Fresh alerts, clipboard and active music take priority over DIY content; quotes fill idle time. Online lyrics are opt-in; native notification access still needs Windows authorization. Export the SDK from the developer panel; source documentation is in plugins-sdk/README.zh-CN.md.

Desktop layers have been removed. Upgrade recovery restores enabled legacy configurations; the explicit `FolderThemeStudio.App.exe --restore-desktop` command remains available.

## Start in three steps

1. Open Icon studio from Home. Choose one of twelve Apple-inspired colors and a material, or import an image. Expand Advanced options to customize gradients, stroke, and glow.
2. Add the folders you want to change. The default scope is selected folders only; the current-user global scope is also available.
3. Calculate the plan, review it, then apply. Saving a personal theme is optional and only needed for reuse. Open Backup & restore from Home to undo the latest application.

## Materials, stickers, and simulated desktop

Choose satin, glass, clay, frosted glass, or paper. Expand Stickers to adjust a sticker's color, size, and rotation or drag it on the preview. Expand size previews or the simulated desktop to inspect the result. Desktop preview choices do not change the saved theme or application plan.

## Import an image icon


1. Choose Imported image icon under Icon source.
2. Select a PNG, JPG, JPEG, BMP, or ICO image. GIF is not supported. For a multi-size ICO, the largest available layer is selected automatically.
3. The whole image becomes the icon; its aspect ratio is preserved, it is centered, and transparent padding is added.
4. Review the preview, calculate the plan, and apply it.
5. The newly imported image or ICO becomes the current icon immediately; no separate save is required.

## Live folders

Pause over an ordinary desktop or Explorer folder for about half a second to preview its five most recently modified regular files. Drag regular files onto the card, then drop them on This folder or a direct child folder to move them. Folders, links, and same-name conflicts are not moved or overwritten.

The same card shows created/modified dates, direct content counts, and a size summary including subfolders. The matching Windows info tip is temporarily dismissed while the folder is recognized; leaving or clicking ends this behavior without changing global preferences. Large directories, unreadable content, or links are marked as partially counted.

## Apply and restore


- Global mode updates ordinary-folder icon mappings for the current user without scanning the whole drive.
- Compatible mode processes only explicit roots. Existing customizations are merged safely; special, protected, or unwritable folders are still skipped.
- Enable “Keep applying this style to new folders” to recursively monitor selected roots and style newly created folders automatically.
- Open Automatic Rules from Home or the sidebar to add a name keyword and icon for a monitored root. Use the current editor style or import an image/ICO.
- Matching is case-insensitive substring matching; rules nearer the top take priority. Unmatched folders keep that root's existing monitoring icon.
- Rules recursively apply to folders created later under the selected root. Rename events keep the existing default-icon behavior; existing folders are not batch-rewritten.
- Open Folder Hover Cards from Home or the sidebar to manage categories, notes, and accent colors. Apple-inspired color swatches are available here too.
- Alt + right-click opens a compact note editor. The clicked folder is selected automatically. Ctrl + Enter saves and closes; Esc cancels. Category and color options are collapsed. Change the shortcut in Settings. This dedicated shortcut replaces the Windows context menu; ordinary right-clicks retain their native behavior.
- The opacity setting applies to all note cards. Category data is stored for the current Windows user. If a folder moves, add its new path. Files, shortcuts, and virtual items do not show cards.
- Closing the main window minimizes to the tray by default. The tray can pause/resume monitoring or exit completely. Monitoring resumes in the background after Windows sign-in; both behaviors can be disabled in Settings.
- Recalculate the plan after changing any input, then apply.
- Restore latest backup undoes the most recent application.
- Main, Settings, and Tutorial use native rounded corners on supported Windows versions while retaining normal title-bar, resizing, and snapping behavior.

## Troubleshooting

- Nothing happens after double-clicking: install the x64 .NET 8 Desktop Runtime or rerun the online installer.
- Icons do not refresh immediately: wait for the Windows icon cache or reopen Explorer windows.
- SmartScreen warning: this Beta is unsigned; verify the downloaded SHA-256.
- The app changes ordinary folder icons only; it does not replace File Explorer.
