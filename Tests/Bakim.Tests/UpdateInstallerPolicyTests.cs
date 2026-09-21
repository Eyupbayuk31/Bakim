using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using Bakım.Services;
using Xunit;

namespace Bakim.Tests
{
    /// <summary>
    /// Güncelleme paketinin hazırlanması ve başlatma hatalarının sınıflandırılmasını doğrular.
    /// Bu davranışlar Uygulama Denetimi (Smart App Control / WDAC) engellemelerinde
    /// kullanıcıya doğru yönlendirmenin gösterilmesini belirler.
    /// </summary>
    public class UpdateInstallerPolicyTests
    {
        private static MethodInfo GetPrivateMethod(string name)
        {
            MethodInfo? method = typeof(AutoUpdateService).GetMethod(
                name,
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            return method!;
        }

        private static int? InvokeExtractWin32ErrorCode(Exception? exception)
        {
            return (int?)GetPrivateMethod("ExtractWin32ErrorCode").Invoke(null, new object?[] { exception });
        }

        [Fact]
        public void ExtractWin32ErrorCode_DogrudanWin32Exception_KoduDondurur()
        {
            // 1260 = ERROR_ACCESS_DISABLED_BY_POLICY
            Assert.Equal(1260, InvokeExtractWin32ErrorCode(new Win32Exception(1260)));
        }

        [Fact]
        public void ExtractWin32ErrorCode_SarmalanmisIstisna_IcKoduBulur()
        {
            // Process.Start hatayı "An error occurred trying to start process..." metniyle
            // sarmaladığında kodun kaybolmadığı doğrulanır; asıl regresyon senaryosu budur.
            var wrapped = new InvalidOperationException(
                "An error occurred trying to start process.",
                new Win32Exception(1260));

            Assert.Equal(1260, InvokeExtractWin32ErrorCode(wrapped));
        }

        [Fact]
        public void ExtractWin32ErrorCode_IcIceGecmisZincir_EnDerinKoduBulur()
        {
            var chain = new InvalidOperationException(
                "dış",
                new AggregateException("orta", new Win32Exception(1223)));

            Assert.Equal(1223, InvokeExtractWin32ErrorCode(chain));
        }

        [Fact]
        public void ExtractWin32ErrorCode_Win32OlmayanIstisna_NullDondurur()
        {
            Assert.Null(InvokeExtractWin32ErrorCode(new InvalidOperationException("win32 değil")));
        }

        [Fact]
        public void ExtractWin32ErrorCode_Null_NullDondurur()
        {
            Assert.Null(InvokeExtractWin32ErrorCode(null));
        }

        [Fact]
        public void GetUpdateStagingDirectory_GeciciDizinKullanmaz()
        {
            var stagingDirectory = (string?)GetPrivateMethod("GetUpdateStagingDirectory").Invoke(null, null);

            Assert.False(string.IsNullOrWhiteSpace(stagingDirectory));

            // Paket %TEMP% altından çalıştırıldığında Akıllı Uygulama Denetimi ve ASR
            // kuralları düşük itibar nedeniyle engelliyor; bu yüzden kalıcı dizin şart.
            string tempPath = Path.GetFullPath(Path.GetTempPath());
            string resolvedStaging = Path.GetFullPath(stagingDirectory!);

            Assert.False(
                resolvedStaging.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase),
                $"Güncelleme paketi geçici dizine indiriliyor: {resolvedStaging}");
        }

        [Fact]
        public void GetUpdateStagingDirectory_BeklenenKlasoruOlusturur()
        {
            var stagingDirectory = (string?)GetPrivateMethod("GetUpdateStagingDirectory").Invoke(null, null);

            Assert.NotNull(stagingDirectory);
            Assert.True(Directory.Exists(stagingDirectory!), "Hazırlık dizini oluşturulmadı.");
            Assert.EndsWith(Path.Combine("Bakim", "Updates"), stagingDirectory!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PurgeStaleInstallers_GuncelPaketiSilmez()
        {
            var stagingDirectory = (string?)GetPrivateMethod("GetUpdateStagingDirectory").Invoke(null, null);
            Assert.NotNull(stagingDirectory);

            string freshPackage = Path.Combine(stagingDirectory!, $"Bakim_Setup_Update_{Guid.NewGuid():N}.exe");
            File.WriteAllText(freshPackage, "test");

            try
            {
                GetPrivateMethod("PurgeStaleInstallers").Invoke(null, new object?[] { stagingDirectory });

                // Eşik bir gün olduğundan yeni oluşturulan paket korunmalıdır.
                Assert.True(File.Exists(freshPackage), "Güncel güncelleme paketi yanlışlıkla silindi.");
            }
            finally
            {
                if (File.Exists(freshPackage)) File.Delete(freshPackage);
            }
        }

        [Fact]
        public void PurgeStaleInstallers_EskiPaketiSiler()
        {
            var stagingDirectory = (string?)GetPrivateMethod("GetUpdateStagingDirectory").Invoke(null, null);
            Assert.NotNull(stagingDirectory);

            string stalePackage = Path.Combine(stagingDirectory!, $"Bakim_Setup_Update_{Guid.NewGuid():N}.exe");
            File.WriteAllText(stalePackage, "test");
            File.SetLastWriteTimeUtc(stalePackage, DateTime.UtcNow.AddDays(-3));

            try
            {
                GetPrivateMethod("PurgeStaleInstallers").Invoke(null, new object?[] { stagingDirectory });

                Assert.False(File.Exists(stalePackage), "Eski güncelleme paketi temizlenmedi.");
            }
            finally
            {
                if (File.Exists(stalePackage)) File.Delete(stalePackage);
            }
        }

        [Fact]
        public void GetSmartAppControlState_CokmedenDurumDondurur()
        {
            // Kayıt defteri anahtarı bulunmayan sistemlerde de akış kırılmamalıdır.
            object? state = GetPrivateMethod("GetSmartAppControlState").Invoke(null, null);

            Assert.NotNull(state);
            Assert.True(Enum.IsDefined(state!.GetType(), state), $"Tanımsız ilke durumu: {state}");
        }
    }
}
