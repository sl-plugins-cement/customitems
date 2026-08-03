# CustomItems

<p align="center">
  <a href="#english"><strong>English</strong></a>
  <span> | </span>
  <a href="#chinese"><strong>中文</strong></a>
</p>

## English

CustomItems is a shared LabAPI helper library for SCP: Secret Laboratory item plugins. It is not a standalone LabAPI plugin: it does not register commands, create config files, show player text, or run by itself. Item plugins reference this DLL to reuse common custom-item mechanics instead of copying the same tracking and held-model code into every plugin.

### What It Provides

- `ItemRegistry<TKind>`: a lightweight serial-number registry that maps vanilla item or pickup serials to a plugin-owned enum. Use it to answer "is this vanilla item one of my custom items?" inside item, pickup, damage, and use-event handlers.
- `HeldMeshManager`: lifecycle management for camera-tracked first-person custom meshes. It can show a mesh over the native viewmodel or hide the native viewmodel and replace it.
- `HeldMeshSpec`, `HeldLightSpec`, and `MeshPrimitive`: neutral data objects for describing AdminToy primitive meshes and optional pulsing core lights.
- `HeldVisualMode`: the display mode for held meshes: `None`, `Overlay`, or `HideAndReplace`.
- `HeldMeshVisual`: the low-level spawned visual implementation. Most plugins should use `HeldMeshManager` rather than constructing this directly.

### Installation

Build the library:

```powershell
dotnet build .\CustomItems.csproj
```

The project targets `net48` and expects SCP:SL managed assemblies under:

```text
C:\Program Files (x86)\Steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed
```

Override that location when needed:

```powershell
dotnet build .\CustomItems.csproj -p:SCP_SL_MANAGED="D:\Servers\SCPSL\SCPSL_Data\Managed"
```

Deploy `CustomItems.dll` to the LabAPI global dependency folder so any plugin can load it:

```text
%APPDATA%\SCP Secret Laboratory\LabAPI\dependencies\global\CustomItems.dll
```

This repository's shared `Deploy.targets` refreshes existing deployed copies after `dotnet build`. A first-time install still needs the DLL copied into `dependencies\global` once.

Plugins that depend on this library should reference `CustomItems.dll` at build time and be deployed normally under:

```text
%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<active port>\
```

### Usage Pattern

For serial tracking, create one registry per plugin or per item domain:

```csharp
private enum SpecialItemKind
{
    AnchorTool,
    MedicTool,
}

private readonly ItemRegistry<SpecialItemKind> _items = new();
```

Mark the serial when your plugin grants, spawns, or identifies a custom item:

```csharp
_items.Mark(item, SpecialItemKind.AnchorTool);
```

Gate event handlers by serial:

```csharp
if (!_items.IsKind(args.Item.Serial, SpecialItemKind.AnchorTool))
{
    return;
}
```

Call `Clear()` on round reset and plugin disable so serials never leak across rounds.

For held custom meshes, build a `HeldMeshSpec` from your plugin's loaded primitives, then own one `HeldMeshManager` in the service that manages the item:

```csharp
private readonly HeldMeshManager _heldMesh = new("MyItem");

_heldMesh.Show(player, spec, HeldVisualMode.HideAndReplace);
```

If `HideAndReplace` is used, the manager forces `player.CurrentItem = null` to suppress the native viewmodel. That creates a `ChangedItem(None)` event. In your changed-item handler, call `AbsorbForcedNone(player)` before treating the deselect as a real unequip:

```csharp
if (args.NewItem == null && _heldMesh.AbsorbForcedNone(args.Player))
{
    return;
}
```

Call `Hide(player)` on real unequip, death, disconnect, or role change. Call `Clear()` on round reset and plugin disable.

### Commands And Config

CustomItems has no Remote Admin commands, player-console commands, Server-Specific Settings, or config file. Commands, permissions, localization, hints, and config remain the responsibility of the plugin that references this library.

### Known Limitations

- The library only supplies shared mechanics. It does not spawn or grant custom items by itself.
- Held meshes are server-side AdminToy primitives and lights. Keep primitive counts modest and avoid repeatedly spawning or destroying meshes in high-frequency code.
- `HideAndReplace` intentionally manipulates the current item to hide the native first-person viewmodel. Plugin code must absorb the manager's forced `ChangedItem(None)` event or it may incorrectly disarm the custom item.
- Mesh cleanup is owned by the caller. Missing `Hide()` or `Clear()` calls can leave toys alive until the round or server cleans them up.
- Client-side UI, inventory names, and native item icons are not changed by this library.

## Chinese

CustomItems 是一个用于 SCP: Secret Laboratory 物品插件的共享 LabAPI 辅助库。它不是独立的 LabAPI 插件：不会注册命令、不会生成配置文件、不会显示玩家文本，也不会单独运行。物品插件引用这个 DLL，用来复用常见的自定义物品追踪和手持模型逻辑，避免每个插件都复制一份实现。

### 提供的功能

- `ItemRegistry<TKind>`：轻量序列号注册表，把原版物品或拾取物的 serial 映射到插件自己的枚举。可在物品、拾取、伤害、使用等事件中判断“这个原版物品是不是我的自定义物品”。
- `HeldMeshManager`：管理跟随摄像机的第一人称自定义手持网格生命周期。可以把模型叠加在原版手持模型上，也可以隐藏原版手持模型并替换成自定义模型。
- `HeldMeshSpec`、`HeldLightSpec`、`MeshPrimitive`：用于描述 AdminToy primitive 网格和可选脉冲核心光源的中立数据对象。
- `HeldVisualMode`：手持模型显示模式：`None`、`Overlay`、`HideAndReplace`。
- `HeldMeshVisual`：底层已生成视觉对象实现。大多数插件应使用 `HeldMeshManager`，不要直接构造它。

### 安装

构建库：

```powershell
dotnet build .\CustomItems.csproj
```

项目目标框架为 `net48`，默认从以下位置读取 SCP:SL 托管程序集：

```text
C:\Program Files (x86)\Steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed
```

需要时可以覆盖路径：

```powershell
dotnet build .\CustomItems.csproj -p:SCP_SL_MANAGED="D:\Servers\SCPSL\SCPSL_Data\Managed"
```

把 `CustomItems.dll` 部署到 LabAPI 全局依赖目录，使任意插件都可以加载它：

```text
%APPDATA%\SCP Secret Laboratory\LabAPI\dependencies\global\CustomItems.dll
```

本仓库的共享 `Deploy.targets` 会在 `dotnet build` 后刷新已经部署过的副本。第一次安装仍需要先把 DLL 手动放进 `dependencies\global`。

依赖此库的插件应在构建时引用 `CustomItems.dll`，并正常部署到：

```text
%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<active port>\
```

### 使用方式

序列号追踪建议每个插件或每个物品域创建一个注册表：

```csharp
private enum SpecialItemKind
{
    AnchorTool,
    MedicTool,
}

private readonly ItemRegistry<SpecialItemKind> _items = new();
```

当插件发放、生成或识别自定义物品时标记 serial：

```csharp
_items.Mark(item, SpecialItemKind.AnchorTool);
```

在事件处理器中按 serial 过滤：

```csharp
if (!_items.IsKind(args.Item.Serial, SpecialItemKind.AnchorTool))
{
    return;
}
```

在回合重置和插件禁用时调用 `Clear()`，避免 serial 跨回合残留。

自定义手持网格需要先把插件加载的 primitive 转成 `HeldMeshSpec`，然后在管理该物品的服务里持有一个 `HeldMeshManager`：

```csharp
private readonly HeldMeshManager _heldMesh = new("MyItem");

_heldMesh.Show(player, spec, HeldVisualMode.HideAndReplace);
```

如果使用 `HideAndReplace`，管理器会通过 `player.CurrentItem = null` 隐藏原版第一人称手持模型。这会触发一次 `ChangedItem(None)` 事件。在你的切换物品事件处理器里，应先调用 `AbsorbForcedNone(player)`，再把这次取消手持当作真正的卸下：

```csharp
if (args.NewItem == null && _heldMesh.AbsorbForcedNone(args.Player))
{
    return;
}
```

真正卸下、死亡、断线或切换角色时调用 `Hide(player)`。回合重置和插件禁用时调用 `Clear()`。

### 命令与配置

CustomItems 没有 Remote Admin 命令、玩家控制台命令、Server-Specific Settings 或配置文件。命令、权限、本地化、提示和配置都由引用此库的具体插件负责。

### 已知限制

- 本库只提供共享机制，本身不会生成或发放自定义物品。
- 手持网格使用服务端 AdminToy primitives 和 light。primitive 数量应保持适中，避免在高频逻辑中反复生成或销毁模型。
- `HideAndReplace` 会刻意修改当前物品来隐藏原版第一人称模型。插件代码必须吸收管理器触发的 `ChangedItem(None)`，否则可能误判为玩家真正卸下了自定义物品。
- 网格清理由调用方负责。漏掉 `Hide()` 或 `Clear()` 可能导致 toys 一直存在，直到回合或服务器清理。
- 本库不会修改客户端 UI、背包物品名称或原版物品图标。
