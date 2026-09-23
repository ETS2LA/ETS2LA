**If your ETS2LA version is below 2026.9.5051, you need to install this update manually by downloading the installer again!**
### ETS2LA C# 2026.9.XXXX
#### UI Updates
- Visualization page is now implemented. Requires `VisualizationSockets` plugin available in the catalogue. This is now a recommended plugin installed during the onboarding.
- You can now do the following actions in the plugin manager:
    - Enable/Disable all plugins.
    - Open the plugin folder in your default file manager.
    - Reload plugins at runtime. This will unload and reload all plugins, mainly useful for developers.
- Notifications will now go transparent if hovered. This allows you to interact with elements below them.
- Onboarding will now make sure the game is closed *before* SDK installation. This should prevent issues with `Memory not available for output` errors.
- Updated the default controls image in the onboarding.
- Added option to keep the ETS2LA window on top of other windows. This can be turned on in Settings - Display.
- Removed redundant settings that were accidentally left in the settings pages.
- Implemented a toggle mode for overlay interaction. **This is now the default behavior.** You can change this in `Settings - Display`.
- You can now refresh catalogue plugins without restarting ETS2LA.
- Added option to completely disable overlay rendering. Please do note that this will break all features that rely on the overlay, including AR and the HUD. This can be changed in `Settings - Display`.
- Display settings now has clearer behaviour when disabling AR rendering or the overlay by disabling their respective settings.
- `Switch` components now have a `Class` field to override their tailwind styling.
- Changing the ETS2LA volume now plays an example sound to preview the volume level.
- Added notification when changing driving modes. This will hopefully alleviate confusion about what mode you are in until the new HUD is implemented.
- Improved updater page header styling.

#### Bug Fixes and Other Changes
- Fixed updater bug.
- Fixed race condition when removing notifications.
- `Memory not available for output` will now describe how the error can be fixed.
- Prevent loading duplicated libraries or plugins. In some cases this could cause a crash on startup.

#### Thank you to today's sponsor... You!
Development is possible thanks to our [sponsors](https://github.com/sponsors/Tumppi066). Please consider supporting if you like the project and have the means to do so.
- McAs13
- luuukske
- Lun011666
- Anonymous
- AkhtiveLzz
- All our [previous sponsors](https://github.com/sponsors/Tumppi066)

---
<!-- Content inside ETS2LA will be cutoff at the line above, do not place lines inside the changelog. -->

> [!CAUTION]
> 如果您来自中国，可访问 [https://cnb.cool/ETS2LA-CN/Euro-Truck-Simulator-2-Lane-Assist/-/releases/latest](https://cnb.cool/ETS2LA-CN/Euro-Truck-Simulator-2-Lane-Assist/-/releases/latest) 这是我们官方的中国镜像仓库，从该仓库下载和安装的文件会优先使用中国仓库进行更新；另外这是开源免费项目，如发现有倒卖/安装收费行为，中国镜像站运营负责人有权利封禁你的ip，如遭遇ip封禁请不要提请任何申诉，我们不会受理  
<sub>The above notice is to make sure Chinese people can find their specific download. They can't download from GitHub without a VPN.</sub>

> [!WARNING]
> If you have issues with your **overlay being fully opaque**, make sure to reinstall your graphics card drivers.
> **NVIDIA** - Set `OpenGL GDI Compatibility` to `Prefer Compatible` in the **NVIDIA App**. Disable `Automatically manage colors for apps` in Windows settings.
> **AMD** - Disable `HDR` and `Automatically manage colors for apps` in Windows settings.

<sub>ETS2LA is version specific, make sure you use a supported version!  
Older versions are not kept compatible with server side changes.</sub>
<!-- Please include a link to the latest working version for each game version. -->
<!-- 1.59, 1.60 and 1.61 share the same map data version -->
| Game Version  |  ETS2LA Version  |
| ------------- | ---------------- |
| **1.61**      | [**≥ 2026.9.5026**](https://github.com/ETS2LA/Euro-Truck-Simulator-2-Lane-Assist/releases/latest)  |
| **1.60**      | [**≥ 3.4.0**](https://github.com/ETS2LA/Euro-Truck-Simulator-2-Lane-Assist/releases/latest) |
| **1.59**      | [**≥ 3.2.0**](https://github.com/ETS2LA/Euro-Truck-Simulator-2-Lane-Assist/releases/latest) |

You can download the **latest version** from here.   
**Don't use this if you're not on the latest game version!**

[![Download ETS2LA](https://a.fsdn.com/con/app/sf-download-button)](https://sourceforge.net/projects/ets2la/files/latest/download)

Or download directly from GitHub below:
| Operating System  |      Installer File       | Additional Requirements |
| ----------------- | ------------------------- | ----------------------- |
| Windows           | `ETS2LA-win-*.msi`        | Included                |
| Linux             | `ETS2LA-linux-*.AppImage` | **GLIBC 2.43**          |

<sub>If you're running your game in Proton, please install the Windows version inside the proton instance.  
Press **_Assets_** below to download the installer.</sub>