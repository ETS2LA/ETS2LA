### ETS2LA C# 3.4.30
- Added `Simplified Graphics` to Display settings. This will turn off shaders and other effects. If you can't see certain overlay elements, try to enable this.
- Tweaked `LineWithGradient` to allow more control over the gradient.
- Added 2D and 3D versions of the `KalmanFilter` class.
- ETS2LA will now continue parsing the game data, even if one of the core SCS files fails to load. This happens on systems where mods are installed into the game directory, rather than the dedicated mod folder. **These mods are not supported and will not be loaded, ensure you load your mods into Documents/Euro Truck Simulator 2/mod, or a similar path.** Fixes `Error: This is a zip file`.
- **airskye1** - Add `Open Catalogue` button to the plugin page if no plugins are installed.

---
<!-- Content inside ETS2LA will be cutoff at the line above, do not place lines inside the changelog. -->

> [!CAUTION]
> 如果您来自中国，可访问 [https://cnb.cool/ETS2LA-CN/Euro-Truck-Simulator-2-Lane-Assist/-/releases/latest](https://cnb.cool/ETS2LA-CN/Euro-Truck-Simulator-2-Lane-Assist/-/releases/latest) 这是我们官方的中国镜像仓库，从该仓库下载和安装的文件会优先使用中国仓库进行更新；另外这是开源免费项目，如发现有倒卖/安装收费行为，中国镜像站运营负责人有权利封禁你的ip，如遭遇ip封禁请不要提请任何申诉，我们不会受理  
<sub>The above notice is to make sure Chinese people can find their specific download. They can't download from GitHub without a VPN.</sub>

> [!WARNING]
> If you have issues with your **overlay being fully opaque**, make sure to reinstall your graphics card drivers.  
> **NVIDIA** - Set `OpenGL GDI Compatibility` to `Prefer Compatible` in the **NVIDIA App**.  
> **AMD** - Disable **HDR** in Windows settings.

<sub>ETS2LA is version specific, make sure you use a supported version!  
Older versions are not kept compatible with server side changes.</sub>
<!-- Please include a link to the latest working version for each game version. -->
<!-- 1.59 and 1.60 share the same map data version -->
| Game Version  |  ETS2LA Version  |
| ------------- | ---------------- |
| **1.61**      | *Not Supported*  |
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
