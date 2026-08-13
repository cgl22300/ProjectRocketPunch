# ProjectR 主动布娃娃原型概述

> 更新日期：2026-08-13  
> Unity：2022.3.62f2c1  
> 当前阶段：单臂玩家控制 + IK 期望姿态 + 三段刚体物理跟随的最小原型

## 1. 接手前必须知道的状态

原型资产已经从 `D:\workspace\NetworkExperience` **复制**到本项目，源项目文件仍然保留。迁移时保留了所有 `.meta`，因此脚本、模型、材质、输入资源和预制体 GUID 理论上仍能正确关联。

用户已确认 `ProjectR` 能在 Unity 中成功打开并进入运行状态。迁移后的资产、Input System 和脚本已经能够由编辑器加载。

迁移验证期间，Unity 批处理模式曾在 Package Manager 阶段出现以下错误，但这已经不是当前阻塞：

```text
Failed to resolve packages: The "path" argument must be of type string. Received undefined.
```

当前 `Packages/manifest.json` 和 `Packages/packages-lock.json` 都已加入：

```json
"com.unity.inputsystem": "1.14.2"
```

该日志仅用于追溯早期迁移过程：

`D:\workspace\ProjectR\Logs\CodexMigrationImport.log`

## 2. 设计目标

这是一个偏物理模拟的角色运动原型。设计重点是：

- 玩家可以分别控制双臂；当前只实现右臂，后续再推广到左臂。
- 双臂保留明显物理性、动量、碰撞和受击反馈。
- 躯干和其他部位只做适量物理或程序联动，避免整个角色过软、站立失败。
- 玩家与敌人最终共享同一套运动机构；差异只在命令来源：玩家 Input、敌人 Agent，未来还可能来自网络同步。
- 动画不是最终控制者。动画、玩家命令和 AI 命令都应生成“期望姿态/运动命令”，再由物理层追赶。
- 玩家手动完成基础动作；基础动作组合未来可触发连招，并临时提高动画辅助权重，使动作更漂亮。
- 当前不做动作识别系统，先用手动参数验证运动机制。

追求的是“符合人类直觉”，不是完整人体生物力学仿真：拳头不能跑到肩后，肘部不能反折，肩和躯干需要有限跟随，接近击中时应有刚度和冲击感。

## 3. 当前操作

输入资源：

`Assets/Demo/MyActiveRagdoll/InputAction/Player Input Actions.inputactions`

当前映射：

- 按住鼠标左键：启用右臂手动控制。
- 鼠标左右移动：控制拳头横向位置/挥拳平面。
- 鼠标上下移动：控制收拳和向前伸拳。
- 鼠标滚轮：控制拳头高度。
- 鼠标前进侧键或 `F`：触发 Animator 的 `Attack` Trigger（如果 Animator Controller 中存在对应参数）。
- WASD / 左摇杆：Input Action 中已有 Movement，但目前没有角色移动组件消费它。

相机是固定观察角度的稳定跟随相机，不读取鼠标旋转。它跟随玩家预制体根节点，并带遮挡 SphereCast。

## 4. 资产位置

### 原型

```text
Assets/Demo/MyActiveRagdoll/
├─ InputAction/
│  └─ Player Input Actions.inputactions
├─ Prefab/
│  └─ Player.prefab
└─ script/
   ├─ InputManager.cs
   ├─ Entity/Entity.cs
   └─ Player/
      ├─ Player.cs
      ├─ MyActiveRagdollCamera.cs
      ├─ SingleArmController.cs
      ├─ SingleArmPoseSolver.cs
      ├─ SingleArmPhysicsDriver.cs
      └─ PhysicsArmImpact.cs
```

### 角色模型

实际使用的模型不是 MyActiveRagdoll 下的模型，而是：

```text
Assets/Demo/PhysicDemo1/OmniManCharacter/
```

`Player.prefab` 嵌套引用：

```text
Assets/Demo/PhysicDemo1/OmniManCharacter/Prefabs/OmniMan.prefab
```

核心蒙皮 FBX：

```text
Assets/Demo/PhysicDemo1/OmniManCharacter/全能侠_带骨.fbx
```

不要再次换成其他 OmniMan 或测试模型，否则已校正的骨骼方向、拳面方向和关节空间可能失效。

## 5. 当前运行时架构

执行链路：

```text
InputManager
    ↓ 玩家原始输入
SingleArmController（ExecutionOrder 25）
    ↓ 期望拳头位置、拳面方向、肘部弯曲方向、躯干辅助
SingleArmPoseSolver（ExecutionOrder 50）
    ↓ 临时求解 Two Bone IK，缓存期望局部旋转，然后恢复动画骨骼
SingleArmPhysicsDriver（ExecutionOrder 100）
    ↓ ConfigurableJoint 肌肉追赶期望旋转
运行时 UpperArm / LowerArm / Hand 刚体
    ↓ 将物理解写回显示骨骼
OmniMan 蒙皮显示
```

### `Entity.cs` / `Player.cs`

`EntityBase` 和泛型 `Entity<T>` 当前只是非常薄的类型基础，没有组合式能力框架。`Player` 也只通过 `RequireComponent` 保证存在 `SingleArmController`。

这是有意保持简单：不要重新引入庞大的组合框架。后续应渐进地把“命令源”和“运动机构”分开，而不是提前建立复杂 ECS/能力系统。

### `InputManager.cs`

负责从 Input System 查找和读取：

- `Gameplay/Movement`
- `Gameplay/MouseXY`
- `Gameplay/MouseScroll`
- `Gameplay/Attack`
- `Gameplay/ArmControl`

以后接入敌人或联网时，不应让物理驱动层直接依赖 Input System。推荐新增一个很薄的命令数据结构/接口，让玩家 Input、Agent 和网络状态都能产生相同运动命令。

### `SingleArmController.cs`

职责是把输入转换成期望目标，不直接做最终骨骼物理解。

关键规则：

- 控制方向以 `Player.prefab` 根 Transform 为基准，不以相机或模型局部骨骼轴为基准。
- 目标由“当前肩膀位置 + 玩家根节点左右/上下/前方偏移”构造。
- Reach 最小值始终大于零，禁止拳头目标进入肩膀后方。
- 肘部提示方向由代码生成：身体外侧、略向下、保持正前分量。
- 拳面方向使用首帧参考姿态计算旋转偏移，避免直接假定 FBX 手骨局部轴。
- 松开鼠标后回到默认收拳参数，`recoverySharpness` 高于普通目标平滑速度。

它还会通过 Humanoid 映射自动取得以下 OmniMan 骨骼并做有限联动：

- Spine
- Chest
- UpperChest
- 对应侧 Shoulder

联动包括躯干扭转、轻微前倾和送肩。所有轴也以玩家根节点为准。

### `SingleArmPoseSolver.cs`

这是临时、自行实现的 Two Bone IK 求解器，不是 Unity Animation Rigging 的 `TwoBoneIKConstraint`。

求解器会：

1. 保存动画骨骼位置与旋转。
2. 临时将上臂、前臂、手旋转到期望姿态。
3. 缓存期望的局部旋转和肩部参考坐标。
4. 恢复原动画骨骼，避免上一帧物理解反馈进入下一帧 IK 输入。

未来可以用 Animation Rigging 生成同样的期望 Pose，但不要让 Rig 直接覆盖最终物理显示骨骼。推荐继续维持“动画/Rig 生成目标，物理层负责落地”的分层。

### `SingleArmPhysicsDriver.cs`

在运行时创建：

- `Player_PhysicalArm`
- `ShoulderAnchor`（未指定 `bodyAnchor` 时创建的运动学刚体）
- `PhysicalUpperArm`
- `PhysicalLowerArm`
- `PhysicalHand`

三段肢体由 `ConfigurableJoint` 连接，通过 Slerp Drive 追赶 IK 缓存的期望局部旋转。

必须保留当前关节空间换算。曾经使用 `LookRotation(joint.axis, joint.secondaryAxis)`，它会额外引入约 90° 偏转，导致肘关节反转和拳面错误。当前实现把：

- `joint.axis` 作为 joint-space X/right
- `Cross(axis, secondaryAxis)` 作为 forward
- 再计算 up

运动阶段会略微降低肌肉倍率，接近完全伸展且速度较高时提高倍率，形成“挥动较松、命中前变硬”的手感。

`bodyAnchor` 当前为空，因此肩部使用运行时运动学锚点。设计上最终应连接到稳定的玩家胶囊刚体，让攻击反作用力进入身体，同时由胶囊维持站立。

### `PhysicsArmImpact.cs`

只挂在运行时创建的物理手部上。碰撞发生时按以下数据生成 `ArmImpactSample`：

- 接触点相对闭合速度
- 有效质量
- 动能
- Unity 碰撞冲量
- 由能量和冲量组合出的原型 Damage

通过 `IArmImpactReceiver` 向目标父级组件转发。攻击者自己的手臂也会收到碰撞反馈并短暂降低肌肉权重，然后逐步恢复。

这还是原型伤害模型，不是最终战斗数值系统。

### `MyActiveRagdollCamera.cs`

固定 yaw/pitch、平滑跟随玩家根节点，自动寻找或创建 Main Camera，并通过 SphereCast 缩短遮挡距离。摄像机不跟随物理手臂或其他抖动骨骼。

## 6. 当前关键预制体参数

以下值已写入 `Assets/Demo/MyActiveRagdoll/Prefab/Player.prefab`：

### 手动目标

- Lateral Sensitivity：`0.0025`
- Reach Sensitivity：`0.0025`
- Wheel Height Sensitivity：`0.001`
- Lateral Limits：`[-0.55, 0.55] × 总臂长`
- Reach Limits：`[0.18, 0.92] × 总臂长`
- Height Limits：`[-0.55, 0.35] × 总臂长`
- Default Lateral：`0.2 × 总臂长`
- Default Reach：`0.28 × 总臂长`
- Default Height：`-0.15 × 总臂长`
- Target Sharpness：`18`
- Recovery Sharpness：`26`

### 身体联动

- Spine Twist：`7°`
- Chest Twist：`12°`
- Upper Chest Twist：`8°`
- Forward Lean：`5°`
- Shoulder Protraction：`0.08 × 总臂长`
- Body Assist Sharpness：`10`

### 物理手臂

- Upper Arm Mass：`2`
- Lower Arm Mass：`1.4`
- Hand Mass：`0.8`
- Limb Radius：`0.065`
- Hand Radius：`0.09`
- Spring：`900`
- Damper：`80`
- Maximum Force：`2500`
- Muscle Weight：`1`
- Moving Muscle Multiplier：`0.82`
- Impact Muscle Multiplier：`1.3`
- Shoulder Limit：`120°`
- Elbow Limit：`145°`
- Wrist Limit：`70°`

## 7. 已解决过的重要问题

- 最初的纯布娃娃角色会立刻倒地：设计上改为稳定胶囊/根刚体作为身体锚点，手臂做重点模拟。最终胶囊尚未在当前 Player 预制体实现。
- 手臂方向曾完全错误：控制坐标已统一为 Player 预制体根节点。
- 鼠标向前曾把手伸到头顶：目标平面和 forward/up 基准已修正。
- 肘关节曾向前反折、拳头朝右后上：修复了 IK Hint 和 ConfigurableJoint joint-space 换算。
- 加入物理后曾重新出现反转：IK 期望 Pose 与物理显示 Pose 已隔离，避免反馈环。
- 相机曾跟随不稳定骨骼：现在只跟随稳定根节点并采用固定观察角。
- 旧输入表造成重复键位风险：迁移前已删除未使用的 `InputExample.inputactions`，只保留当前精简输入表。
- 已删除无效辅助对象：`RightHandTargetMaker`、骨骼子级 `RightElbowHint`、左右 `Hand_middle` 标记。

## 8. 当前缺口和风险

按优先级排序：

1. **Player 还没有稳定胶囊 Rigidbody。** 当前只有肩部运动学锚点，不是完整角色身体。
2. **身体联动仍是直接骨骼旋转/位移原型。** 尚未进入统一 Pose 管线，也没有物理躯干。
3. **只有右臂。** 左右手切换、双臂同时存在和各自输入尚未实现。
4. **当前 IK 是临时自实现算法。** 后续计划使用 Animation Rigging Two Bone IK 生成期望 Pose，但尚未接入。
5. **动画融合只是接口原型。** `animationAssistWeight` 默认是 0，连招系统和基础动作组合系统都未实现。
6. **没有正式整理的专用 Demo Scene。** 当前主要资产是 `Player.prefab`；应确认用户当前运行场景是否已保存并纳入版本管理。
7. **Movement Action 尚未驱动角色。** 玩家不能通过当前 MyActiveRagdoll 代码移动胶囊。
8. **伤害与受击反馈只是样本。** 没有生命值、受击动画/物理脉冲或敌人实现。

## 9. 推荐接手顺序

### 第一步：固化当前可运行基线

1. 清空并检查 Console，记录仍存在的 Warning。
2. 确认当前运行场景已保存到 `Assets` 并纳入版本管理。
3. 记录当前 Play Mode 的正确操作结果，最好保存一张截图或短视频作为回归基线。
4. 检查 `Player.prefab` 是否存在 Missing Script 或丢失引用。

### 第二步：建立稳定身体锚点

给 Player 根节点增加 CapsuleCollider + Rigidbody（或单独稳定 BodyRoot），约束角色保持竖直。把 `SingleArmPhysicsDriver.bodyAnchor` 连接到该刚体，验证出拳反作用力不会让整个角色立即倒地。

### 第三步：改善单臂动作直觉

先继续做好右臂，不急着复制左臂：

- 调整收拳默认位。
- 将直拳和勾拳的目标轨迹限制为可理解的曲面/弧线。
- 让骨盆、胸口、肩膀按出拳阶段联动，而不是只按 Reach 线性联动。
- 区分蓄力、加速、接近命中、回收阶段。
- 用接触前手部线速度和身体参与程度控制击打感。

### 第四步：替换 Pose 生成层

用 Animation Rigging 的 Two Bone IK 或 Rig Builder 生成期望姿态，但保留现有分层：

```text
命令 → 目标轨迹 → Animation/Rig 期望 Pose → 物理肌肉 → 最终显示 Pose
```

不要让动画和物理在同一组最终骨骼上无序覆盖。

### 第五步：抽象命令源

在确有玩家、敌人两个消费者时，再提取小型命令结构，例如：

```csharp
struct ArmMotionCommand
{
    public bool Active;
    public float Lateral;
    public float Reach;
    public float Height;
    public float AnimationAssist;
}
```

玩家 Input、敌人 Agent 和未来网络层只负责产生命令；共享运动机构消费命令。避免提前搭建庞大的组合能力框架。

## 10. 修改原则

- 渐进迭代，优先保持一个右臂原型可玩、可调、可验证。
- 不要把当前系统重构成复杂组合式框架。
- 不要再次根据相机朝向计算手臂方向；使用 Player 根节点。
- 不要把 IK Hint 放到受驱动手臂骨骼子级。
- 不要直接恢复旧的 ConfigurableJoint `LookRotation(axis, secondaryAxis)` 算法。
- 不要让物理输出成为下一帧期望 Pose 的输入。
- 新增空 GameObject 标记不会自动参与 Unity 骨骼蒙皮计算；只有蒙皮权重绑定的骨骼会影响网格。辅助目标应由代码/Rig 明确读取。
- 修改预制体或模型引用时必须保留 `.meta` 和 GUID。
- 每轮修改至少完成 C# 编译；涉及骨骼、关节或预制体时必须再做 Play Mode 目视验证。

## 11. 原始来源

本项目当前原型从以下项目复制而来：

```text
D:\workspace\NetworkExperience
```

源项目中对应目录：

```text
Assets/Demo/MyActiveRagdoll
Assets/Demo/PhysicDemo1/OmniManCharacter
```

迁移是复制，不是移动。若需要对照迁移前状态，可以只读比较源项目；后续正式开发应以 `D:\workspace\ProjectR` 为准，避免同时修改两份造成分叉。
