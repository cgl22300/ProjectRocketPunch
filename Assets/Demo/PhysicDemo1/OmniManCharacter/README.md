# Omni Man 第三人称角色

## 可用资源

- `Prefabs/OmniManThirdPerson.prefab`：带完整控制、相机和动画逻辑，直接拖入场景即可。
- `Prefabs/OmniMan.prefab`：只包含模型、材质、Animator 和动画驱动，适合自行编写玩法。
- `Animations/OmniMan.controller`：Idle/Run 移动混合，以及 Punch/Hit 动作状态。
- `Materials/OmniMan.mat`：自动匹配当前 Built-in/URP 管线的 PBR 材质。

## 动画状态机

导入后的动画名称：

- `idel.fbx` → `Idle`，循环。
- `run.fbx` → `Run`，循环。
- `punch.fbx` → `Punch`，单次播放。
- `受击.fbx` → `Hit`，单次播放。

Animator 参数：

- Float `Speed`：0 为 Idle，移动时过渡到 Run。
- Bool `Grounded`：当前是否接地，预留给后续跳跃/下落动画。
- Trigger `Attack`：播放 Punch。
- Trigger `Hit`：播放受击动作。

Punch 和 Hit 结束后自动返回 Locomotion。

## 操作

- `WASD` / 手柄左摇杆：移动。
- `Shift` / 按下手柄左摇杆：加速。
- `Space` / 手柄南键：跳跃。
- 鼠标左键 / 手柄西键：攻击。
- `H` / 手柄右肩键：测试受击动画。
- 按住鼠标右键拖动 / 手柄右摇杆：旋转视角。
- 鼠标滚轮：缩放视角。

角色使用 `CharacterController` 和新版 Input System，不依赖 InputActionAsset 或 Cinemachine。
场景没有标记为 `MainCamera` 的相机时，相机脚本会自动创建一个。

## 重新生成

Unity 菜单：`Tools > Omni Man > Rebuild Character Resources`。
