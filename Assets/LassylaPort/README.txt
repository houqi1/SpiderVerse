Lassyla 材质节点移植 / Unity 6 URP

入口
  当前场景：Assets/LineArt/LineArtPreview.unity
  新模型：Spider-Gwen Lassyla
  Prefab：Assets/LassylaPort/Spider-Gwen_Lassyla.prefab
  共用父材质：Assets/LassylaPort/Materials/Lassyla_OriginalNodes.mat
  菜单：Tools > Lassyla > Select Shared Material / Frame New Spider Gwen

不规则边缘参数（父材质调整会应用到身体与头套）
  Fresnel / IOR                         边缘随观察角度变化的强度
  Edge Noise / Scale                   缺口尺寸；数值越大，缺口越细
  Fresnel Ramp / Black、White Position  边缘透明影响范围
  Noise Ramp / Dark、White Position     哪些噪声区域成为缺口
  Noise Ramp / Dark Value               原值为线性 0.003952979，不能替换为 0
  EEVEE Alpha Hash / Scale              抖动粒度；原静止视图值 0.1
  Diagnostic View                      单独显示透明度、噪声、Fresnel、Generated

源码对应
  Fresnel 使用完整介电公式，背面采用 1/IOR，保留全反射。
  Noise 使用 Blender 的 Jenkins 整数哈希、梯度 Perlin 和归一化 fBM。
  两条线性色带相乘得到透明度，透明度大于 EEVEE alpha hash 阈值时丢弃。
  没有用 Schlick、Unity 随机噪声、屏幕空间 Bayer 或普通 Alpha Clip 替换这些节点。
  Generated 坐标逐部件从 Blender 原始纹理空间提取并存入模型副本 UV 通道 2。
  身体与头套保留原 FBX 骨骼、绑定数据和权重；新实例采用与 Blender 相同的静止姿势。
  姿势由全部可见部件的 bind pose 共同还原，实际蒙皮矩阵误差记录在 rest-pose-validation.txt。
  同时包含原始两组 blurry voronoi、法线混合、颜色混合和 257 像素色带。
  色带使用当前未保存 Blender 状态中的第一点 0.1145454496。

范围
  原 Blender 窗口使用 forest.exr 材质预览和 AgX。角色使用从该摄影棚采集的漫反射响应和动态自阴影。
  蒙皮表面的 Object 坐标先从世界坐标还原到原网格空间，Generated 仍按部件取原始网格值。
  LassylaEdgeFeature 在 Scene 和 Game 中重建抖动透明。原截图拍摄时时间线处于播放状态。
  LineArt_Renderer 中的 Match Timeline Playback 对应播放预览，用 8 相位取样和 YCoCg 历史重投影。
  关闭该选项可检查暂停预览，按 16 次采样、滤波半径 1.5 和曝光 0 累积。
  透明颗粒与背景在线性空间合成，然后通过 Blender OCIO 配置生成的 AgX LUT 显示。
  透明阈值使用转换回 Blender 的世界坐标；相机抖动用 Halton 2/3，透明阈值用 Halton 7。
  Film 使用上游 Gaussian/Blackman-Harris 拟合权重和半精度写入舍入规则。
  注意：Blender pow2f(exposure) 是 exposure 的平方；本工程曝光 0 时亮度权重为常量。
  原生 50% 透明平面已用于验证此行为，结果见 Tools/LassylaUnity/film-probe.log。
  播放对照在原工程的静止骨骼姿势下完成。独立渲染的帧在颗粒位置和局部阴影上仍有差异。
  渲染功能位于 LineArt_Renderer 的 Lassyla EEVEE Transparency；材质依赖此功能。
  这次修正前的 Unity 参数保存在 Tools/LassylaUnity/material-before-visual-fix.json。
  当前工程覆盖全画面的 Motion Vector Debug 已关闭以显示真实材质。
  调试显示开启前的 renderer 备份：Tools/LassylaUnity/LineArt_Renderer.before.asset
  新模型加入前的场景备份：Tools/LassylaUnity/LineArtPreview.before.unity

复现与证据
  当前彩色对照入口：Tools/LassylaUnity/visual/color/compare.html
  该页提供用户原截图的同坐标裁切、实际播放中 Blender 窗口的六个角度、Unity 同背景和 Unity 城市场景。
  同背景测试只使用空摄影棚作为背景，各视角的角色由 Unity 场景里的蒙皮模型绘制。
  菜单 Tools > Lassyla > Match Blender Screenshot View 可回到截图视角。
  旧的白色透明链路测试：Tools/LassylaUnity/visual/compare.html
  0/45/90/135/180/270 度：近景 16 样本、近景 64 样本、全身 64 样本。
  Blender 参考由 LiveReference.blend 的原生材质渲染；Unity 图来自实际蒙皮模型与 Renderer Feature。
  对照统一把发光颜色设为白色以隔离透明边缘，保留原节点的透明链路。场景保持彩色材质。
  图片并未调整对比度或补画；差异视图直接使用两张原始 PNG。
  图像测量记录 comparison-report.json 不作为“完全一致”的自动验收结论。
  前景遮挡检查图：Tools/LassylaUnity/visual/unity-occlusion-0.png。
  Blender 实时快照与完整节点数据：Tools/LassylaUnity/LiveReference.blend、material.json
  参数/坐标：Tools/LassylaUnity/parameters.json
  节点 GPU 对照：Tools/LassylaUnity/validation.json
  数值参考由 Blender 原生节点直接计算；不使用本移植代码生成期望值。
  高度扭曲的高频 Noise 中间值在 CPU/GPU 间仍有约 0.000141 的最大浮点差异；
  validation.json 保留这项严格检查的失败结果，未将阈值放宽。
  groupOutputsPassed 单独检查组的实际 Color/Position 输出；edgePassed 检查请求的边缘。
  验证菜单：Tools > Lassyla > Validate Against Blender Nodes
  Python 导出：Tools/LassylaUnity/prepare_data.py
  HLSL 生成：Tools/LassylaUnity/generate_core.py
  精确上游版本：Blender 5.2.3 LTS / e13b728c6880
  上游源码与哈希：Tools/LassylaUnity/source-manifest.json

许可
  Blender 移植函数沿用原文件 GPL-2.0-or-later；见 COPYING.txt 及源码中的 SPDX。
  模型与作者工程的权利仍属于各自权利人。
