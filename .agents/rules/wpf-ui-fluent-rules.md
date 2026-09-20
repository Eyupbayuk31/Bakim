---
trigger: always_on
description: "Full UI Modernization & Wpf.Ui Fluent Standards for Bakım"
---

# Bakım Projesi - Wpf.Ui Fluent Modernizasyon Standartları

Bu proje genelindeki tüm arayüz geliştirmelerinde **Wpf.Ui (Fluent UI for WPF)** kütüphanesi temel alınır:

1. **Pencere & Başlık Mimarisi:**
   - Standart `Window` yerine `ui:FluentWindow` (`xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`).
   - `WindowBackdropType="Mica"`, `ExtendsContentIntoTitleBar="True"`.
   - Standart başlık çubuğu yerine `ui:TitleBar` kullanımı zorunludur.

2. **Wpf.Ui Bileşenleri:**
   - Standart `Button` -> `ui:Button` (`Appearance="Primary/Secondary/Success/Danger"`, `Icon="{ui:SymbolIcon ...}"`).
   - Standart `TextBox` & `PasswordBox` -> `ui:TextBox` & `ui:PasswordBox` (`PlaceholderText`, `ClearButtonEnabled="True"`, `VerticalContentAlignment="Center"`, `Padding="12,10"`).
   - Standart `Border` ile kartlama -> `ui:Card`, `ui:CardAction`, `ui:CardExpander`.
   - İkonlar -> `ui:SymbolIcon` (`Symbol="LockClosed24"`, `Symbol="Shield24"`, `Symbol="Person24"`, `Symbol="Search24"`, `Symbol="Delete24"`, `Symbol="Dismiss24"` vb.).
   - Durum & Bildirimler -> `ui:InfoBar`, `ui:ProgressBar`, `ui:ProgressRing`.

3. **Hizalama & Esnek Grid:**
   - Sabit `Width`/`Height` ve yapay `Margin` kaydırmaları yasaktır. Oransal `Grid` (`*` ve `Auto`), `Padding` ve `HorizontalAlignment="Stretch/Center"` esastır.
   - Metinlerin dikeyde tam ortalanması için `VerticalContentAlignment="Center"` her form ve buton elemanında tanımlanır.

4. **Tema & Renk Paleti:**
   - `Wpf.Ui.Appearance.ApplicationThemeManager` kullanılır.
   - Tüm renkler kütüphanenin dinamik semantik fırçalarından çekilir (`ThemeResourceDictionary`, `CardBackgroundFillColorDefaultBrush`, `TextFillColorPrimaryBrush`, `AccentTextFillColorPrimaryBrush` vb.).

5. **Sıfır AI Hissiyatı & Kurumsal Profesyonellik (Anti-AI Aesthetic):**
   - **Saçma Sapan İkon Yasağı:** Arayüzde rastgele, uyumsuz, çocuksu veya yapay zeka yapımı olduğu bağıran emojiler ve saçma ikonlar KESİNLİKLE KULLANILAMAZ.
   - **Yalnızca Resmi Fluent/Windows Sembolleri:** Sadece Windows 11 yerel tasarım diline ait `ui:SymbolIcon` (Fluent 2 sembol kütüphanesi) veya `Segoe MDL2 Assets` font sembolleri kullanılır. İkon boyutları standart ve dengeli (16, 20 veya 24) tutulur.
   - **Tasarım Dili:** Tüm görünüm Microsoft'un kendi üst düzey mühendislerinin elinden çıkmış gibi ciddi, kurumsal, koyu slate temasıyla tam uyumlu ve profesyonel olmak zorundadır.

