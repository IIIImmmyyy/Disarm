# Disarm 解码与别名审计（2026-10-29）

本轮先补齐经过编码验证的 `UNIMPLEMENTED` 分支，再按用户追加要求核查现有解码与别名。实现位于独立的 Disarm Git 子仓库，基线提交为 `ebba74914139a5f0e32b8f1d99dfbdfdc785211d`。本轮没有修改 UREngine 的 ARM64→ISIL、IR Pass 或 CodeGen。

## 编码依据与验证边界

- 编码与保留域依据固定版本的 LLVM 官方源文件：[AArch64InstrFormats.td](https://github.com/llvm/llvm-project/blob/llvmorg-20.1.8/llvm/lib/Target/AArch64/AArch64InstrFormats.td)、[AArch64InstrInfo.td](https://github.com/llvm/llvm-project/blob/llvmorg-20.1.8/llvm/lib/Target/AArch64/AArch64InstrInfo.td)。
- 使用 Capstone 5.0.7 独立反汇编，Keystone 0.9.2 汇编可支持的基线指令；不通过 Disarm 自己生成期望结果。
- 新增解码范围检查了 5,716 个编码：1,750 个有效编码的助记符及完整操作数一致，3,924 个保留/未分配编码保持拒绝，42 个有效编码属于下表明确保留的复数/BF16/结构访存/表查找范围，继续返回 `UNIMPLEMENTED`。纯显示差异仅规范化大小写、十六进制、SP/ZR、内存偏移写法和等价的零扩展别名，不忽略寄存器、lane、移位量或寻址模式。
- Capstone 对两种未分配 TME 形态的六个测试编码泛化显示为 `MRS S0_...`；这里依据 LLVM `TMSystemI` 的固定域判为未分配，未将工具输出视为架构定义。
- 现有实现另检查了 21,030 个编码，其中独立反汇编器识别 20,078 个有效编码。覆盖逻辑移位、加减移位/扩展、全部合法 W/X bitfield 的 immr/imms 组合、条件选择、部分整数访存、向量置换/提取和浮点操作。它是专项差分审计，不能证明整个 A64 指令集均正确。
- 数据与执行工具保留在 `%TEMP%\UREngine\disarm-completion-20261029`，包括 `make_corpus.py`、`make_existing_corpus.py`、`audit_examples.py`、`decoder/`、`completed-comparison.json` 和 `existing/examples-*.json`。

## 原有 24 处 UNIMPLEMENTED 逐项处理

同一个返回位置可能代表整个指令族，不能把返回位置数当作未实现指令数。16 处已补齐或扩展；其余 8 处保留，其中两处是不可达的旧 fallback。

| 原位置/指令族 | 本轮处理 | 保留边界 |
| --- | --- | --- |
| DataProcessing1Source / PAUTH | 补齐 PAC/AUT 双寄存器、零 modifier、XPACI/XPACD | W 形式及保留 opcode 仍拒绝 |
| Hints / CRm 非零 | 补齐已核对命名别名、BTI target、通用 `HINT #imm7` | 新架构未命名别名保留原始 HINT 编码 |
| LoadsStores / exclusive register | 补齐 LDXR/STXR、acquire/release、B/H/W/X | status 始终是 W，load 不伪造 status |
| LoadsStores / CAS | 补齐 CAS/CASP 与 ordering/宽度 | CASP 展开完整两对寄存器和地址；拒绝奇数 pair |
| LoadsStores / ordered | 补齐 LDAR/STLR、LDLAR/STLLR | 访问宽度和目标 W/X 分开建模 |
| LoadsStores / MemoryCopyOrSet | 保留 | MOPS 尚无完整实现与验证 |
| LoadsStores / LdarpOrStlr | 补齐 LRCPC2 LDAPUR/STLUR | 有符号未缩放 imm9，保留域拒绝 |
| LoadsStores / atomic memory | 补齐 LSE RMW/SWP 与 LDAPR | 超出已核对 LSE 表的后续扩展继续拒绝 |
| LoadsStores / PAC load | 补齐 LDRAA/LDRAB | 还原分散 signed imm10、八字节缩放、offset/pre-index |
| LoadsStores / unprivileged | 补齐 LDTR/STTR | 包括 signed B/H/S 的实际 W/X 目标 |
| LoadsStores / exclusive pair | 补齐 LDXP/STXP、LDAXP/STLXP | 不丢第二寄存器和 W status |
| ScalarAdvancedSimd / Copy | 保留不可达代码 | 入口已被 Arm64Simd.AdvancedSimdScalarCopy 抢先消费，不是额外指令缺口 |
| SVE | 保留 | 需要 scalable vector/predicate 等操作数模型及完整指令族验证 |
| NonScalarAdvancedSimd / shift immediate | 补齐常规移位、累积、舍入、饱和、窄化/扩宽、定点转换 | 保留 Q/immh/size/opcode 的架构约束 |
| NonScalarAdvancedSimd / Copy 尾部 | 保留不可达代码 | 前面的 imm4 switch 及每条合法路径均已 return/throw |
| NonScalarAdvancedSimd / table lookup | 保留 | TBL/TBX 最多需要四个表寄存器，现有模型没有寄存器列表；不能只输出首寄存器 |
| NonScalarAdvancedSimd / three-same FP16 | 补齐 24 种常规运算 | 未核对的新 FAMAX/FAMIN 编码继续未实现 |
| NonScalarAdvancedSimd / two-reg misc FP16 | 补齐转换、取整、零比较、unary | 零比较保留浮点零操作数 |
| NonScalarAdvancedSimd / three-reg extension | 补齐 RDM、SDOT/UDOT/USDOT、SMMLA/UMMLA/USMMLA | BF16、复数及后续扩展继续未实现 |
| NonScalarAdvancedSimd / across lanes | 补齐 11 种整数/浮点归约 | 目标 scalar 与源 vector 宽度分别保留，包括 FP16 |
| System / WithResult | 补齐 TSTART/TTEST | 本组不是 SYSL；按 TME 固定编码核对 |
| Simd / wide crypto | 保留 | SHA512/SM3/SM4 等组需要单独完整核对 |
| Simd / single structure | 保留原支持范围 | 原有单寄存器 LD1 lane 已支持；其余结构访存需要列表/replicate 模型 |
| Simd / structure post-index | 保留 | 需同时完整表达列表、lane、立即数/寄存器 writeback |

同时修正了这批实现所必需的入口分流：B/H exclusive/CAS 不再进入 SIMD structure；exclusive/ordered/CAS 读取真实 o2/o1 位，不再测试六位字段中不存在的 bit6。`Arm64Instruction.ToString` 现在消费既有第五操作数，CASP 的地址不再丢失。

已修复原样本的 `0x6EA0B842`：它是整数 `NEG V2.4S, V2.4S`，原实现错误返回浮点 `FCMGE`。

## 追加核查：现有解码/别名中确认的问题

以下问题已用真实编码复现，且区分了启用/关闭别名的结果。表中保留首次复现结果；ORR/SXT 和 CMP 显示已经按后续批准的别名范围修复，见下方追加验证记录。其余解码问题仍未修改。

| 编码 | 正确指令 | 首次核查结果 | 原因与归属 |
| --- | --- | --- | --- |
| `AA0117E0` | `ORR X0, XZR, X1, LSL #5` | `MOV X0, X1, LSL` | Arm64Aliases 第一条 ORR→MOV 检查 Op2Imm，实际 shift 在 Op3Imm，误将有移位的值当作直接拷贝并清掉移位量 |
| `AA011420` | `ORR X0, X1, X1, LSL #5` | `MOV X0, X1, LSL` | 同寄存器 ORR→MOV 规则未证明移位为零。X1=1 时正确结果为 33，MOV 为 1 |
| `93402020` | `SBFX X0, X1, #0, #9` | `INVALID`；关闭 alias 后可得到正确 SBFM | Arm64Aliases 的 SXT 匹配接受 imms=7..31 整段，但 switch 只接受 7/15/31；其他合法宽度抛异常 |
| `4E025820` | `UZP2 V0.16B, V1.16B, V2.16B` | `TRN1` | AdvancedSimdPermute opcode 表错配 |
| `4E022820` | `TRN1 V0.16B, V1.16B, V2.16B` | `ZIP2` | 同上；正确表为 001/010/011/101/110/111 → UZP1/TRN1/ZIP1/UZP2/TRN2/ZIP2 |
| `4E026820` / `4E027820` | `TRN2` / `ZIP2` | `INVALID` | 同一 opcode 表遗漏合法 110/111 |
| `6E1F0820` | `EXT V0.16B, V1.16B, V31.16B, #1` | Rm 变成 `V15` | AdvancedSimdExtract 把五位 Rm 用四位掩码提取，V16–V31 均截断 |
| `6EA1F820` | `FSQRT V0.4S, V1.4S` | `FRSQRTE V0.4S, V1.4S` | 普通 S/D two-register-misc 表将 sqrt 错映成 reciprocal sqrt estimate，两者语义不同 |
| `4EA18820` | `FRINTP V0.4S, V1.4S` | `FRINTN V0.4S, V1.4S` | 同一普通 S/D misc 表漏区分 size 高位，取整模式错误 |
| `4E218820` | `FRINTN V0.4S, V1.4S` | `FRINTN V0.16B, V1.16B` | 将浮点 size 当作普通整数 size，操作数 lane 错误；部分 2D 形式也被错误拒绝 |
| `39800020` | `LDRSB X0, [X1]` | `INVALID` | unsigned-immediate 路径已选择 LDRSB，但后续 registerBase switch 缺 LDRSB；unscaled 的 LDURSB 有同类遗漏 |
| `0B0D80FD` | 未分配：W ADD 的 imm6=32 | 接受为 `ADD W29, W7, W13, #32, LSL` | AddSubtractShiftedRegister 缺少 sf=0 时 imm6 高位的架构检查；逻辑移位路径已有相应检查 |

另外 `EB02143F`（`CMP X1, X2, LSL #5`）曾显示为 `CMP X1, X2, LSL`，但 Op3Imm 仍保存 5。它属于别名/显示契约缺陷；本次核对消费者读取 Op3 的既有约定后，只修正显示，不移动元数据。

专项检查中的全部 15,360 个合法 bitfield 编码并不都输出首选别名。例如 UBFM/SBFM 和某些 UBFIZ/UBFX 的文本差异可以语义等价，未把这些差异一概当成误解码；CINV/CSETM 未别名化也不是运算错误。上述表只列已确认的操作、寄存器、位宽、合法性错误或单独标注的显示问题。

ORR/SXT 的别名前提已校正。剩余问题建议按 owner 修复普通 S/D misc 与 permute/EXT 表，以及 signed-load 的目标寄存器分支和 W 移位非法编码检查，继续沿现有解码模型修正，不在 SSA/CodeGen 中兜底。

## 验证结果

在 UREngine 仓库根目录执行：

```powershell
dotnet build .\UREngine\UREngine.csproj --no-restore --verbosity minimal
dotnet test .\Disarm\Disarm.Tests\Disarm.Tests.csproj --no-restore --verbosity minimal
.\UREngine\bin\Debug\net10.0\UREngine.exe regress --suite .\UREngine.Cpp2CS.Tests\Regression\cpp2cs.regressions.json --config .\UREngine\testcases.local.json
.\UREngine\bin\Debug\net10.0\UREngine.exe run --case GTA --config .\UREngine\testcases.local.json --type SanAndreasUnity.Importing:SanAndreasUnity.Importing.RenderWareStream.TextureNative --output "$env:TEMP\UREngine\disarm-completion-20261029\texture-native-output"
```

- 构建成功，0 error；原项目报告 236 warning。
- Disarm 单测 84/84 通过，新增 17 个编码/操作数契约用例。此前 16 个在实现前复现失败，另一个 unused-Rs 用例固定 LLVM 已记录的 Unpredictable 字段处理。生成 C# 快照无法观察这些 decoder 内部契约，因此使用直接解码测试。
- 全量 structured regression：451/451 通过，0 失败；290 个 class 均成功。未修改 approved snapshot 或回归断言。
- TextureNative 构造函数：从 GTA ELF 读取 `0x200DFA4` 起的 `0x424` 字节，265 条指令均可解码，零 `INVALID/UNIMPLEMENTED`；`SHL/NEG/SSHL/CMHI/BSL/ADDV` 与前一轮 IDA ARM64 锚点一致。
- TextureNative focused run 仍失败，但失败已推进到下一层：`0x200E310` 的 SHL 缺少 ARM64→ISIL Handler。该层没有在本轮修改；因此没有为尚未生成、编译并经 IDA 复核的构造函数添加 approved snapshot 或伪造样本成功结果。
- 诊断日志：`%TEMP%\UREngine\disarm-completion-20261029\engine-build.log`、`disarm-tests.log`、`full-regression.log`、`texture-native-run.log`。临时证据集中保存在此目录；仓库根目录没有本轮生成的 `artifacts*`。

## 别名修复追加验证（2026-10-29）

实现仅修改 `Arm64Aliases` 和 `Arm64Instruction.ToString`，复用现有操作数槽位，不改变 IR、flags 消费者或 Pass 顺序：

- ORR→MOV 的零源及同源规则检查真实的 `Op3Imm`，有非零移位时保留 ORR 和全部源操作数；无移位 scalar/SIMD MOV 仍保留原源视图。
- SXTB/H 只匹配 imms=7/15；SXTW 只匹配 imms=31 且为 X 目的寄存器。其他合法 SBFM 继续匹配 SBFX/SBFIZ/ASR，不再抛 `Impossible imm3`，也不生成不存在的 `SXTW Wd,Wn`。SBFIZ 使用架构的 imms<immr 选择条件，immr=0 的抽取使用 SBFX。
- CMP 显示从原 Op3 读取 shift/extend 的量；Op2 空洞及 Op3 保留约定不变，与 FlagsProcessHandler 已有消费者一致。

直接解码测试新增 6 个契约用例：修复前 5 失败、1 个控制项通过，修复后 Disarm 全部 90/90 通过。对原有 21,030 编码专项语料复核，44 个此前被拒绝的合法 signed bitfield 编码均恢复。对全部 5,120 个合法有符号 bitfield 编码做 30,720 次位向量结果比较，对 ORR 零源/同源/移位形态做 1,280 次结果比较，零差异。

构建使用上节同一命令，结果 0 warning、0 error。完整 structured regression 重新运行后 451/451 通过，290 个 class 全部成功，零失败；未更新快照或断言。诊断文件位于 `%TEMP%\UREngine\alias-fix-20261029`：`before.log`、`disarm-tests.log`、`build.log`、`alias-validation.json` 和 `full-regression.log`；`validate_aliases.py` 保存独立的位向量检查程序。
