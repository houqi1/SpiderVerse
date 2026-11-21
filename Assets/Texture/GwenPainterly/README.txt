Spider-Gwen — 35 号笔刷底色与多层接缝过渡版

当前底色
Gwen_Painterly_BaseColor_4K.png：4096×4096，16-bit RGBA PNG，sRGB。
九张已确认的 UV0 绘画贴图转换到角色现有 UV1 图集，包含备用蒙面头部。
SourceUV/ 保存这九张分部件贴图，可供后续编辑；Toon 材质仍引用共用图集。
部件内部沿用 35 号笔刷布局和不超过 5% 的逐笔明暗变化。
黑白接缝使用同一 35 号笔刷，按邻近内部笔触的尺寸调整，在总宽度约 1.5 个当地笔刷短轴宽度内，以十二层不同深浅颜色叠加，形成两端连续过渡。
其他颜色接缝保留已确认版本。图集透明度与导入前逐像素一致，UV 岛外保留 20px 延展。

现有绑定
_BaseMap 使用底色图集，_BaseMapUV = 1。
_WrapNormalMap 继续使用 Gwen_Painterly_NormalOS_4K.png，线性 RGB、Default 类型，_WrapNormalUV = 1。
模型空间法线、Toon Shader、九个配套材质、原有贴图 GUID 和角色 UV1 网格保持原样。
原 FBX 与 Assets/Texture 下的原始 UV0 图片未替换。

本版工程与检查记录
Tools/GwenPainterly/UVBrushPass/Tip35_LayeredBW5/BlenderPreview/Gwen_Tip35_LayeredBW_Live.blend
Tools/GwenPainterly/UVBrushPass/Tip35_LayeredBW5/strokes.json
Tools/GwenPainterly/UVBrushPass/Tip35_LayeredBW5/atlas_validation.json
Tools/GwenPainterly/UVBrushPass/Tip35_LayeredBW5/UnityImport/installation.json
UnityImport/UnityImport.report.txt 记录 Unity 导入器、材质引用及预制体图集 UV 的检查结果。
UnityImport/BeforeImport_*/ 保存导入前的底色图集、meta、说明和图集预览。
BaseColorAtlasPreview.png 已更新；目录中其他早期对比图属于历史版本。

统一材质参数
Gwen_Painterly_ToonBase.mat 是 Materials/ 下九个部件材质的共同父材质。
共用底色图集、模型空间法线、Toon 阴影与其他共用参数在这个父材质上调整。
子材质仅保留各自 SP 贴图、蒙面头部的 Color Mask 及 Mix 差异。
Tools/GwenPainterly/MaterialHierarchy/hierarchy.report.txt 记录转换及参数继承验证；同目录 Before_*/ 保存转换前材质。

最新窄接缝版本：Tools/GwenPainterly/UVBrushPass/Tip35_NarrowBW15/
背面对比：BlenderPreview/Back_BeforeAfter.png；肩胛及腰侧细节同目录。
本次更新维持共同父材质和九个子材质的继承关系。

最新双端连续过渡版本：Tools/GwenPainterly/UVBrushPass/Tip35_ContinuousBW15/
黑色部件原有 Base Color 乘色已在线性空间写入纹理；该子材质现在继承父材质白色 Base Color，避免接缝二次压暗。
法线、Toon Shader 和父子关系保持；两端色阶递进及背面对比见本版 final_verification.json 与 BlenderPreview/。
