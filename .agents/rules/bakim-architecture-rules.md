---
trigger: always_on
description: "Bakım Projesi - Sistem, Mimari, Güvenlik, UI/UX, Debugging ve Visual Refinement Ana Manifestosu"
---

# Bakım Projesi - Ana Mimari, Güvenlik, UI/UX, Debugging & Visual Refinement Manifestosu

Bu proje, aşağıdaki temel depo ve beceri standartlarını referans alarak geliştirilmektedir:
1. **UI/UX Intelligence & Visual Refinement:** `nextlevelbuilder/ui-ux-pro-max-skill` (Senior WPF UI/UX Specialist)
2. **C# & WPF Best Practices & Exception Handling:** `PatrickJs/awesome-cursorrules`
3. **System Architecture, Patterns & Log Analysis:** `danielmiessler/fabric`
4. **Agent Execution & Refactoring:** `obra/superpower`
5. **General Developer Prompts:** `f/awesome-chatgpt-prompts`

---

## MODULE 1: ADVANCED UI/UX & FLUENT DESIGN (Senior Visual Refinement)
- **Renk Paleti & Derinlik (Slate Dark):**
  - Ana Arka Plan: `#0F172A` (Koyu Slate)
  - Kartlar & Input Kutuları: `#1E293B` (Yüzey Slate)
  - Sınır Çizgileri: `#334155` (Soft, göze batmayan ince kenarlıklar)
  - Vurgu Rengi (Accent): `#38BDF8` veya `#2563EB` (Fluent Mavi / Cyan)
  - Metin Renkleri: Başlıklar `#F8FAFC`, ikincil metinler `#94A3B8`, sessiz metinler `#64748B`.
- **Tipografi & Hizalama Hassasiyeti:**
  - Girdi Kutuları: `VerticalContentAlignment="Center"` ve `Padding="12,10"` standarttır.
  - Köşe Kavisleri: Tüm `Border`, `Button` ve `TextBox` bileşenlerinde `CornerRadius="8"` zorunludur.
  - Odaklanma (Focus State): Sert dış çizgiler yerine `BorderBrush="#38BDF8"` yumuşak odaklama uygulanır.
- **İkonografi:** Standart eski emojiler yerine native Windows 11 `Segoe MDL2 Assets` font ikonları (`FontFamily="Segoe MDL2 Assets"`) kullanılır.
- **Flexible Layout (Sabit Ölçü Yasağı):** Rastgele sabit `Width`/`Height` ve devasa `Margin` kaymaları KESİNLİKLE YASAKTIR. Grid (`*` ve `Auto`) ve esnek hizalama esastır.
- **Dynamic Themes:** Hard-coded renk kodları yazılamaz; `{DynamicResource Brush...}` ile tam dinamik koyu/açık mod uyumu korunur.

---

## MODULE 2: C# & MVVM CLEAN ARCHITECTURE (awesome-cursorrules)
- **Zero Code-Behind Yasağı:** `.xaml.cs` dosyalarına iş mantığı (business logic), disk tarama veya sistem temizleme kodları YAZILAMAZ. Tüm durum yönetimi `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`) ile ViewModel katmanında kurgulanır. View (.xaml.cs) sadece saf görsel etkileşimler ve pencere navigasyonu ile sınırlıdır.
- **Async Non-Blocking UI:** Disk tarama, Temp temizleme, RAM boşaltma, servis yönetimi veya Registry okuma gibi TÜM sistem I/O işlemleri `async/await` ve `Task.Run` ile dikey olarak arka plan iş parçacıklarında çalıştırılır. Arayüz ASLA kilitlenmeyecek ("Yanıt Vermiyor" durumuna düşmeyecektir).
- **Virtualization:** Binlerce dosyayı listelerken bellek şişmesini ve arayüz donmasını engellemek için `ListBox`, `ListView` ve `DataGrid` bileşenlerinde `VirtualizingStackPanel.IsVirtualizing="True"` ve `VirtualizationMode="Recycling"` zorunludur.

---

## MODULE 3: SYSTEM SECURITY & UAC (sec-rules)
- **Safe I/O & İstisna Yönetimi:** Dosya ve klasör silme işlemlerinde `UnauthorizedAccessException` ve `IOException` (kullanımda olan kilitli dosyalar) durumları tek tek `try-catch` ile yakalanmalıdır. Bir dosya silinemediğinde tüm işlem asla çökmez; dosya atlanır ve raporlanır.
- **Kritik Sistem Koruması:** `System32`, `SysWOW64`, `WinSxS`, `Drivers`, `Boot` veya sürücü kök dizinleri (`C:\`, `C:\Windows`) kontrolsüz şekilde müdahale edilemez, temizleme kapsamına ALINAMAZ. Yalnızca güvenli geçici dizinler hedeflenir (`%TEMP%`, `Windows\Temp`, `Prefetch`, `SoftwareDistribution\Download`, Tarayıcı Önbellekleri, `CrashDumps`, `WER`).
- **UAC Yönetimi:** Sistem seviyesinde yetki gerektiren operasyonlar `UacHelper.cs` üzerinden denetlenir. Standart kullanıcıya bilgilendirme sunulur ve gerektiğinde yetki yükseltme ("Yönetici Ol") imkanı sağlanır.

---

## MODULE 4: STEP-BY-STEP AGENT EXECUTION (superpower & fabric)
- Kod üretmeden önce adımlar kısa bir özet halinde planlanır.
- Karmaşık sistem işlemlerinde güvenlik denetimi ön planda tutularak adım adım ilerlenir.
- Her aşamada derleme (`dotnet build`) ve çalışma doğrulaması gerçekleştirilir.

---

## MODULE 5: BUG FIXING & DEFENSIVE PROGRAMMING (debugging-skills)
1. **Kök Neden Analizi (Root Cause):**
   - Hata mesajı / Stack Trace incelenerek hatanın kök sebebi açıkça tespit edilir.
2. **Aşamalı Çözüm (Step-by-Step Fix):**
   - Minimum ve en güvenli kod değişikliği uygulanır. Async/Await kilitlenmeleri ve I/O çakışmaları çözülür.
3. **Koruyucu Kod (Defensive Programming):**
   - Null-check (`?.`, `??`), güvenli casting (`as`), sınır kontrolleri ve kapsamlı `try-catch` blokları eklenir.
   - UI thread güvenliği (`Dispatcher.Invoke`) ve küresel çökme engelleyiciler (`DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`) uygulanır.
