## 说明

普遍情况下 `Charts` 内**每个子文件夹即为一首歌**，
如果某个子文件夹的 `if` 内写有 `"pack": true`，
这个文件夹将以**曲包**模式加载

**曲包**模式下，包内每个直接子文件夹仍是一首歌，
歌曲与 `Charts` 内的正常文件夹格式相同，
但包内不再接受 `pack`

加载后，若存在自制谱则选曲界面最上方会出现合集 **IFCL**，其后再按 `id` 排列各个自定义曲包

## `if` 文件示例：

### 曲包

#### 基础版
```json
{
  "pack": true,                   //启用曲包加载
  "id": "1stMagicNeko",           //曲包id，需要尽可能地保证它是唯一的，若重复则不加载曲包
  "name": "MagicNeko",            //曲包名
  "jacket": ["jkt.png", "bg.png"]  //曲包封面与曲包背景（背景可选，但建议设定）
  //建议比例 [92:37, 72:31]
}
```
无曲包背景
```json
{
  "jacket": "jkt.png"
  //建议比例 92:37
}
```

#### 拓展版
```json
{
  "pack": true,                 //启用曲包加载
  "id": "1stMagicNeko",         //曲包id，需要尽可能地保证它是唯一的，若重复则不加载曲包
  "name": ["魔法猫咪", "魔法貓咪", "MagicNeko", "魔法の猫", "我不会"],
  //曲包名 [简体中文, 繁体中文, English, 日本語, 한국어]
  "jacket": ["jkt.png", "bg.png", null, "small.png", null]
  //[曲包封面,曲包背景,未选中状态封面,右侧小封面,歌曲选择界面封面]
  //建议比例 [92:37, 72:31, 27:8, 35:9, 43:21]
  //可用null占位（占位默认使用曲包封面图）
}
```

#### 文件夹示例：
```
Charts
      `-- Pack
          |-- bg.png
          |-- chart1
          |   |-- if
          |   `-- ...
          |-- chart2
          |   |-- if
          |   `-- ...
          |-- if
          |-- jkt.png
          `-- small.png
```

### 曲目

#### 基础版
```json
{
  "id": "20260929052150xingwei",              //谱面id，需要尽可能地保证它是唯一的，若重复则不加载谱面
  "name": "music",                            //曲名
  "composer": "I don’t know",                 //曲师
  "song": "music.mp3",                        //歌曲文件，接受ogg、mp3、wav
  "illust": "I don’t know",                   //曲绘画师；为可选参数
  "jacket": "jkt.png",                        //曲绘，只接受png，建议为正方形
  "background": "bg.mp4",                     //谱面内的背景，接受png与mp4；为可选参数
  "preview_seconds": ["0", "60"],             //预览音频时间[开始,结束]，以秒计算
  "charter": "Xing_W",                        //谱师
  "chart": "chart.spc",                       //谱面文件，不兼容demo格式
  "lv": 15                                    //谱面难度
}
```
谱面位于Forbidden难度

#### 拓展版
```json
{
  "id": "20260929052150xingwei",
  //谱面id，需要尽可能地保证它是唯一的，若重复则不加载谱面
  "name": ["音乐", "音樂", "music", "音楽", "我不会"],
  //曲名 [简体中文, 繁体中文, English, 日本語, 한국어]
  "composer": "I don’t know",
  //曲师
  "song": "music.mp3",
  //歌曲文件，接受ogg、mp3、wav
  "illust": ["I don’t know!", null, null, "hey?"],
  //曲绘画师[Minimal,Evolved,Ultimate,Forbidden]，
  //可用null占位（占位后当前等级不展示画师）；为可选参数
  "jacket": ["jkt.png", "jtk.png", "kjt.png", "ktj.png"],
  //曲绘[Minimal,Evolved,Ultimate,Forbidden]，只接受png，建议为正方形
  "background": [null, null, "bg.png", "bg.mp4"],
  //谱面内的背景[Minimal,Evolved,Ultimate,Forbidden]，接受png与mp4，
  //可用null占位（占位后当前等级回归默认背景）；为可选参数
  "preview_seconds": ["0", "60"],
  //预览音频时间[开始,结束]，以秒计算
  "charter": ["Skytarry", "MagicNeko", null, "Xing_W"],
  //谱师[Minimal,Evolved,Ultimate,Forbidden]
  //对应难度的谱面为null的情况下可用null占位
  "chart": [null, null, null, "chart.spc"],
  //谱面文件，不兼容demo格式[Minimal,Evolved,Ultimate,Forbidden]
  //可用null占位
  "lv": [1, null, 1, 15]
  //谱面难度[Minimal,Evolved,Ultimate,Forbidden]
  //对应难度的谱面为null的情况下可用null占位
}
```
拓展说明：
```json
"chart": "chart.spc", "charter": "Xing_W", "lv": 15
```
等同于
```json
"chart": [null, null, null, "chart.spc"],
"charter": [null, null, null, "Xing_W"],
"lv": [null, null, null, 15]
```
因此，以下写法合法：
```json
{
  "charter": ["Skytarry", "MagicNeko", null, "Xing_W"],
  "chart": "chart.spc",
  "lv": 15
}
{
  "charter": ["Skytarry", "MagicNeko", null, "Xing_W"],
  "chart": "chart.spc",
  "lv": [1, null, 1, 15]
}
{
  "charter": "Xing_W",
  "chart": "chart.spc",
  "lv": [1, null, 1, 15]
}
```

#### 文件夹示例：
```
Charts
      `-- chart
          |-- bg.mp4
          |-- bg.png
          |-- chart.spc
          |-- dummy.spc
          |-- if
          |-- jkt.png
          |-- jtk.png
          |-- kjt.png
          |-- ktj.png
          |-- music.mp3
          |-- temp.spc
          `-- untitled.spc
```