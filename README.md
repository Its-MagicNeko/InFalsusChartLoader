# InFalsusChartLoader

In Falsus的自制谱加载Mod，使用[MelonLoader](https://github.com/LavaGang/MelonLoader)或[BepInEx](https://github.com/BepInEx/BepInEx)加载

---

## 构建

需要 [.NET SDK](https://dotnet.microsoft.com/download)（目标框架 `net6.0`）

```cmd
build.bat                  :: MelonLoader Release（默认）
build.bat Debug            :: MelonLoader Debug
build.bat --BepInEx        :: BepInEx Release
build.bat --BepInEx Debug  :: BepInEx Debug
```

构建产物位于 `bin/`

---

## 安装

### MelonLoader

1. 安装 [MelonLoader](https://github.com/LavaGang/MelonLoader)
2. 获取 `InFalsusChartLoader.dll`
3. 放进游戏目录下的 `Mods` 文件夹
4. 启动游戏

### BepInEx

1. 安装 [BepInEx](https://github.com/BepInEx/BepInEx)
2. 获取 `InFalsusChartLoader.dll`
3. 将dll放进`GAME_PATH\BepInEx\plugins`
4. 启动游戏

自制谱放在游戏根目录的 `Charts` 文件夹（即 `GAME_PATH/Charts`，若不存在则会自动创建）

---

## 使用

mod会严格按照引导文件 `if` 进行加载，详见[if文件示例文档](./IF.EXAMPLE.md)

if文件只要有一条信息非法则不会导入该谱面

需要查看完整日志时请使用**Debug构建**

---

## 致谢

- [MelonLoader](https://github.com/LavaGang/MelonLoader) —— mod 加载器
- [BepInEx](https://github.com/BepInEx/BepInEx) —— mod 加载器

## 免责声明

本仓库为非官方第三方项目，与 In Falsus、lowiro limited 及其关联公司、子公司、许可方或其他相关实体均无任何隶属、合作、赞助、认可、授权或其他关联关系，如有侵权请联系作者删除