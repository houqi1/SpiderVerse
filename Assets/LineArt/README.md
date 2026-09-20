# Unity Object Line Art 使用说明

迁移位置：`Assets/LineArt/`。适配 Unity 6000.0.48f1 / URP 17，默认使用 RenderGraph，也提供 URP Compatibility 路径。

## 打开与调整

1. 打开 `Assets/LineArt/LineArtPreview.unity`。这是 SampleScene 的副本，角色通过 `Object Line Art` 对象上的 Source 组件指定。
2. 菜单 `SpiderVerse > Line Art > Select Stroke Settings` 选择描边设置；也可以在 `LineArt_Renderer.asset` 的 `Object Line Art` Renderer Feature 上调整。
3. 在 Texture 字段拖入纹理资源，支持 Unity 已导入的 TGA、PNG 等 Texture2D。带透明度的纹理保留 Alpha；白底黑色笔刷勾选 Dark On White Mask。可调 Texture Strength 和 Texture Repeats。
4. Length Randomness 为双向随机：0.4 对应裁剪后长度的 60%–140%。以合并后的完整可见线条为单位，两端对称缩短或按端点切线增长。Closed loop 在固定接缝处处理。随机值不随帧刷新。
5. Thickness 为渲染目标像素宽度。End Taper 控制端点收细程度，Thickness Transition 控制过渡曲线。Offset 的正 X 向右、正 Y 向下。宽度、过渡、纹理和偏移直接更新材质，不重建线条。

## 在其他对象/场景使用

- 给根物体添加 `Object Line Art Source`。Renderers 留空时包含子级 MeshRenderer 和 SkinnedMeshRenderer，也可以指定列表。
- 模型导入设置需要开启 Read/Write。预览安装器自动处理角色引用的模型；纹理无需 Read/Write。
- 相机选择 `LineArt_Renderer`（预览安装器把它追加到 PC/Mobile URP Asset 的 Renderer 列表，默认 Renderer 不变）。也可以手工把 `ObjectLineArtFeature` 加入自己的 Renderer。
- Shader 字段保留 `ObjectLineArt.shader` 引用，确保构建时保留 Shader。
- `SpiderVerse > Line Art > Create or Update Preview Scene` 可以重建接线；不会覆盖已存在预览场景里的其他对象。原 SampleScene、PC_Renderer 上的原描边配置不被替换。

## 实现与性能

- 主线程获取静态网格/蒙皮 BakeMesh 快照；焊接拓扑按 Mesh 缓存。
- 后台 Task 做轮廓/折痕/材质边界/三角形交线提取，48×48 屏幕空间网格遮挡区间裁剪和邻接线条合并。交线候选由 AABB BVH 筛选。
- 可见线条生成带 previous/next 与笔画 UV 的三角带；Shader 每帧按当前相机展开像素宽度。纹理按整条笔画映射，兼容随机长度和粗细曲线。
- Update Rate 限制快照/更新频率；同一相机最多一个后台任务。镜头、姿态和输入不变时复用结果。Scene/Game 相机分别缓存。
- 动画蒙皮快照与 Mesh 上传仍在主线程，高面数角色建议降低 Update Rate；需要时关闭 Intersections、限制 Occluder Layers。任务延迟会造成运动中线稿短暂滞后。
- CPU 遮挡使用可读的 MeshRenderer/SkinnedMeshRenderer 不透明三角形；不把 Alpha Cutout 纹理孔洞、Terrain、粒子当作精确几何遮挡。
- 与网页一致：可见性在偏移和艺术化增长之前确定。延伸线和偏移线不再次做遮挡裁剪。
- 非 XR 单视图实现，未做立体渲染/WebGL 线程平台适配。运行时直接修改 Mesh 拓扑需重新创建 Renderer Feature（常规骨骼/BlendShape 动画不需要）。

## 验证

`SpiderVerse > Line Art > Run Geometry Checks` 检查随机长度上下界、对称切线延伸、曲线 UV、遮挡区间、近裁剪、焊接/合并和闭环纹理接缝。


## 编辑模式预览

不需要进入 Play。后台任务完成、参数调整和相机变化后会按需刷新；静止时不会持续请求重绘。

- **Game 视图**：相机的 Rendering → Renderer 选择 LineArt_Renderer，保持 Source 组件启用。
- **Scene 视图**：URP 17 固定使用当前 URP Asset 的默认 Renderer，不跟随 Main Camera 的 Renderer 选择。要看到线稿，在当前质量等级对应的 URP Asset（PC_RPAsset 或 Mobile_RPAsset）Renderer List 中将 LineArt_Renderer 设为 Default，并勾选 Feature 的 Show In Scene View。这也会影响选择 Use Pipeline Settings 的其他相机；需要原效果的相机请显式选择原 Renderer。
- 编辑模式不会自动播放 Animator；可以通过 Animation 窗口预览姿态。
