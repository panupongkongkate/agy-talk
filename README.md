# 🎙️ agy-talk

> แอป Android คุยเสียงกับ AI ภาษาไทย — กดปุ่ม พูด รอ ~5 วิ ได้คำตอบเป็นเสียง
> พร้อมบทสนทนาต่อเนื่อง ถามต่อได้เรื่อย ๆ ในหัวข้อเดียวกัน

| | |
| --- | --- |
| เวอร์ชันปัจจุบัน | **2.0** (โค้ด v6) |
| แพลตฟอร์ม | Android 7.0+ (ทดสอบจริงบน Infinix Hot 12, Android 12) |
| เทคโนโลยี | .NET 10 · MAUI · C# (UI ล้วน ไม่มี XAML) |
| ใบอนุญาตใช้งาน AI | บัญชี TH-AI Passport / Gemini Enterprise — **ฟรี ไม่มี API key** |

---

## ✨ ฟีเจอร์

- 🎙️ **กดพูดได้เลย** — ถอดเสียง (STT) บนเครื่องแบบเรียลไทม์ เห็นคำถอดขึ้นสด ๆ ตอนพูด เงียบ 1.5 วิ ส่งอัตโนมัติ
- 💬 **คุยต่อเนื่อง** — จำบทสนทนา 12 ข้อความล่าสุด ถามต่อในหัวข้อเดิมได้ ปุ่ม 🔄 เริ่มบทใหม่ หรือเงียบเกิน 10 นาทีแอปลืมเอง
- 🔊 **ตอบเป็นเสียงไทย** + แสดงบันทึกแชทเลื่อนย้อนดูได้
- ❤️ **เช็คสุขภาพระบบ** — แถบล่างจอ ping เซิร์ฟเวอร์ให้ตลอด (เขียว = พร้อม)
- ⏱️ บอกเวลาที่ใช้ตอบแต่ละรอบ

## 🏗️ สถาปัตยกรรม

```
┌─────────────────────┐        ┌──────────────────────────┐
│  📱 agy-talk (MAUI) │  HTTP  │  🖥️ mycom (เครื่องเสริม)  │
│                     ├───────►│  FreeAPI bridge :8792    │
│ · STT บนเครื่อง     │tailnet │        │                 │
│ · บทสนทนาในแอป      │        │        ▼                 │
│ · TTS อ่านคำตอบ     │        │  TH-AI / Gemini          │
└─────────────────────┘        │  (บัญชีเดิม ฟรี)          │
                               └──────────────────────────┘
┌─────────────────────────────────────────────┐
│ 💻 notebook — เซิร์ฟเวิร์เวอร์เล็ก :8791        │
│ โฮสต์ไฟล์ APK ให้มือถือโหลดอัปเดต (AgyServer) │
└─────────────────────────────────────────────┘
```

ทุกอย่างวิ่งผ่าน **Tailscale VPN ภายในบ้าน** — ไม่พอร์ตไปอินเทอร์เน็ต และคำถาม-คำตอบไม่ผ่านคนกลางอื่น

## ⚡ ความเร็ว (วัดจริง)

| เวอร์ชัน | เส้นทาง | เวลา/รอบ |
| --- | --- | --- |
| v1.0 | ส่งไฟล์เสียงให้ AI ฟังเอง | ~21 วิ |
| v1.4 | ถอดเสียงบนมือถือ → ส่งข้อความ | ~13.5 วิ |
| **v2.0** | **ยิงเข้า bridge บน mycom ตรง** | **~5.5 วิ** 🏆 |

## 📁 โครงสร้างโปรเจกต์

```
agy-talk/
├── AgyApp/                    ← แอปมือถือ (MAUI, net10.0-android)
│   ├── MainPage.cs            ← UI + บทสนทนา + ยิง API
│   ├── ISpeechToText.cs       ← interface ถอดเสียง
│   └── Platforms/Android/
│       ├── SpeechToText.cs    ← SpeechRecognizer (ไทย, partial, เงียบจบ)
│       ├── Recorder.cs        ← ตัวอัดไฟล์ .m4a (สำรอง, ไม่ได้ใช้แล้ว)
│       └── AndroidManifest.xml
├── AgyServer/                 ← เซิร์ฟเวอร์บน notebook (โฮสต์ APK)
└── README.md
```

## 🔨 วิธีบิลด์ (บน notebook เท่านั้น)

**สิ่งที่ต้องมี:** Visual Studio 2026 (MSBuild), JDK 26, ไฟล์ `agytalk.keystore` (ดูหมวดความปลอดภัย)

```bash
JAVA_HOME="C:\Program Files\Java\jdk-26.0.2" \
"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" \
/d/repos/agy-talk/AgyApp/AgyApp.csproj -p:Configuration=Release \
-p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=D:/repos/agy-talk/agytalk.keystore" \
-p:AndroidSigningKeyAlias=agytalk \
-p:AndroidSigningKeyPass=●●● -p:AndroidSigningStorePass=●●●
```

⚠️ **กับดักที่เจอมาแล้ว**
- ห้ามใช้ `dotnet build` (พังเสมอ) — ต้องเป็น MSBuild ของ VS
- พาธ keystore ใน bash ต้องใช้ `/` เท่านั้น (`D:\\` จะโดนกิน)
- ห้ามแจก APK แบบ **Debug** (เด้งตายทันที) — Release เซ็นแอปเท่านั้น
- ขึ้นเวอร์ชันใหม่: แก้ `ApplicationVersion` + `ApplicationDisplayVersion` ใน `AgyApp.csproj` — ป้ายในแอปดึงอัตโนมัติ

## 📲 วิธีอัปเดตแอปบนมือถือ

1. ก๊อป APK จาก `bin/Release/.../` → `server-run/wwwroot/agytalk.apk` (notebook)
2. `scp ... infinix-hot-12:storage/downloads/agytalk.apk`
3. บนมือถือเปิด Termux → `termux-open /sdcard/Download/agytalk.apk`
4. มือถือโหลดเองได้ตลอดที่ **http://100.73.5.82:8791/agytalk.apk**

## 🛠️ การดูแล

| อาการ | สาเหตุ | วิธีแก้ |
| --- | --- | --- |
| ⚠️ Session has expired | session TH-AI หมดอายุ (~ทุก 7 วัน) | ตาม runbook ใน wiki หน้า `freeapi-bridge-mycom` (login ที่ notebook → ส่ง auth ไป mycom → รีสตาร์ท task) |
| ❌ หา bridge ไม่เจอ | mycom ดับ / bridge ตาย | เปิด mycom — task `FreeAPIBridge` รันเองตอนล็อกอิน |
| APK โหลดไม่ได้ | notebook ดับ | เปิด notebook — AgyServer รันเองตอนล็อกอิน (Startup folder) |

## 🔐 ความปลอดภัย

- **`agytalk.keystore` ไม่ถูกเก็บใน git** (อยู่ใน `.gitignore`) — เป็น private key เซ็นแอป หาย = อัปเดตแอปต่อไม่ได้ ให้**สำรองแยกต่างหาก**; รหัสผ่านเก็บใน wiki ภายในของเครื่อง (หน้า `agy-talk-maui-app`) ไม่ใส่ไว้ใน repo นี้
- ใน repo **ไม่มี password / token / API key ใด ๆ** (สแกนแล้ว) — ตัวแอปคุยกับ bridge แบบไม่ต้องใช้ key
- IP ที่ปรากฏในโค้ดเป็น IP ภายใน Tailscale (100.64.0.0/10) ใช้ได้เฉพาะในเครือข่ายบ้าน ไม่เข้าถึงได้จากอินเทอร์เน็ต
- ถ้าจะเผยแพร่ repo นี้สาธารณะในอนาคต: ตรวจซ้ำเรื่อง IP ภายในก่อน

## 📜 ประวัติเวอร์ชัน

| เวอร์ชัน | โค้ด | ไฮไลต์ |
| --- | --- | --- |
| 1.0 | 1 | อัดไฟล์เสียงส่งให้ AI ฟังเอง (~21 วิ) |
| 1.1 | 2 | ถอดเสียงบนมือถือ + ส่งข้อความ (13.5 วิ) |
| 1.2 | 3 | ป้ายเวอร์ชัน + สถานะเชื่อมต่อ + จับเวลา |
| 1.3 | 4 | กู้คำถอดเสียงเมื่อ STT จบด้วย error |
| 1.4 | 5 | กู้คำถอดเสียงเมื่อ STT ส่งผลชุดว่าง (พฤติกรรม Infinix) |
| **2.0** | 6 | ยิง bridge บน mycom ตรง (~5.5 วิ) + บทสนทนาต่อเนื่อง + หน้าจอแชท + เช็คสุขภาพระบบ |

---

สร้างและดูแลโดย HP + Claude Code · พ.ย. 2026
