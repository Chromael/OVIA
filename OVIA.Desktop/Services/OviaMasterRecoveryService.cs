using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace OVIA.Desktop
{
    public sealed class OviaMasterRecoveryContext
    {
        public string CompanyId { get; set; }
        public string UserId { get; set; }
        public string SessionPassword { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }

    /// <summary>
    /// 별도 보관하는 OVIA.Master.exe와 1회성 challenge-response 방식으로 통신합니다.
    /// 일반 배포 OVIA에는 공개키만 포함하며, master 계정/고정 비밀번호/개인키는 포함하지 않습니다.
    /// </summary>
    public static class OviaMasterRecoveryService
    {
        private const string RecoveryArgumentPrefix = "--ovia-master-pipe=";
        private const string ProtocolVersion = "OVIA_MASTER_V1";
        private const string RecoveryUserId = "OVIA.Master";
        private const int ConnectTimeoutMilliseconds = 10000;
        private const int MaxPipeNameLength = 96;
        private const int MaxCompanyIdLength = 100;
        private const int MaxSessionPasswordLength = 64;

        // 공개키는 복구 요청의 서명을 검증하는 용도만 사용합니다.
        // 이 값만으로 복구 서명을 생성하거나 관리자 권한을 발급할 수 없습니다.
        private const string PublicKeyXml = @"<RSAKeyValue><Modulus>uCHb2h2rEdJb70zfiXe4NNy2R7JQFJFa31JFOHkGolINNBUW4CXHj1vGbC2c+fEc3C8BjUUTZqQc4E24wicOeAomOLqoKXU25QMAGbtukn9H0tQ8RjS/iL0mezggEIpPMeeGN0bqYQSZZINSlNas3StMUAOzSaWWlZObO26VAiBmp97o5vGkP/z2SxeP/IK/Dn4OTG2aHbmv0XjY46yoDLdflK6uZZ3SzQK8CbjyTwa1awmutAspcDaFOgeYECUls0375qSX7iU60g810uscVHpaZFoaVy02seTFddpdX4/DwsJuCTHWJZMIJ3uGrpxYPY/YRZoV0MOsWgy3lqURIsdtE+ZwgXlkZCZFvTJZftrfLXlemziCy6LOv+tNacYgwruKmBI9z+4krDzswNchg14tn3Uq2kPhXbBD0DKDUgTEVimsiz9Qg/NE+SF/E0wDCPiOzgSwTIawKezvLWWJZuH4m60N6vte4I6Z0qb0zQYayWyxoHok2Mo9Ohjg3wYz</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public static bool HasRecoveryArgument(string[] args)
        {
            string ignored;
            return TryGetPipeName(args, out ignored);
        }

        public static bool TryAuthorize(
            string[] args,
            out OviaMasterRecoveryContext context,
            out string errorMessage)
        {
            context = null;
            errorMessage = "";

            string pipeName;
            if (!TryGetPipeName(args, out pipeName))
            {
                errorMessage = "OVIA Master 복구 요청 정보가 없습니다.";
                return false;
            }

            if (!IsSafePipeName(pipeName))
            {
                errorMessage = "OVIA Master 복구 통신 정보가 올바르지 않습니다.";
                return false;
            }

            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.None))
                {
                    pipe.Connect(ConnectTimeoutMilliseconds);
                    pipe.ReadMode = PipeTransmissionMode.Byte;

                    using (StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true))
                    using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
                    {
                        writer.AutoFlush = true;

                        string challenge = CreateChallenge();
                        string request = BuildRequest(challenge);

                        writer.WriteLine(request);

                        string response = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(response))
                        {
                            errorMessage = "OVIA Master가 복구 서명 응답을 반환하지 않았습니다.";
                            return false;
                        }

                        OviaMasterRecoveryContext parsedContext;
                        string verificationError;
                        if (!TryVerifyResponse(request, response, out parsedContext, out verificationError))
                        {
                            errorMessage = verificationError;
                            writer.WriteLine("FAIL");
                            return false;
                        }

                        context = parsedContext;
                        writer.WriteLine("SUCCESS");
                        return true;
                    }
                }
            }
            catch (TimeoutException)
            {
                errorMessage = "OVIA Master와의 복구 통신 시간이 초과되었습니다.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = "OVIA Master 복구 통신에 실패했습니다.\r\n\r\n" + ex.Message;
                return false;
            }
        }

        private static bool TryGetPipeName(string[] args, out string pipeName)
        {
            pipeName = "";

            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] == null ? "" : args[i].Trim();
                if (!arg.StartsWith(RecoveryArgumentPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pipeName = arg.Substring(RecoveryArgumentPrefix.Length).Trim().Trim('"');
                return pipeName != "";
            }

            return false;
        }

        private static bool IsSafePipeName(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Length > MaxPipeNameLength)
            {
                return false;
            }

            for (int i = 0; i < pipeName.Length; i++)
            {
                char ch = pipeName[i];
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string CreateChallenge()
        {
            byte[] bytes = new byte[32];

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        private static string BuildRequest(string challenge)
        {
            int processId = Process.GetCurrentProcess().Id;
            long utcTicks = DateTime.UtcNow.Ticks;
            string machineName = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(Environment.MachineName ?? ""));

            return ProtocolVersion
                + "|" + challenge
                + "|" + processId.ToString()
                + "|" + machineName
                + "|" + utcTicks.ToString();
        }

        private static bool TryVerifyResponse(
            string request,
            string response,
            out OviaMasterRecoveryContext context,
            out string errorMessage)
        {
            context = null;
            errorMessage = "";

            string[] parts = response.Split('|');
            if (parts.Length != 7 || !string.Equals(parts[0], "OK", StringComparison.Ordinal))
            {
                errorMessage = "OVIA Master 복구 응답 형식이 올바르지 않습니다.";
                return false;
            }

            string companyId;
            string userId;
            string sessionPassword;

            try
            {
                companyId = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                userId = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
                sessionPassword = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
            }
            catch
            {
                errorMessage = "OVIA Master 복구 응답의 사용자 정보가 올바르지 않습니다.";
                return false;
            }

            long issuedTicks;
            long expiresTicks;

            if (!long.TryParse(parts[4], out issuedTicks) || !long.TryParse(parts[5], out expiresTicks))
            {
                errorMessage = "OVIA Master 복구 응답의 유효시간 정보가 올바르지 않습니다.";
                return false;
            }

            DateTime issuedUtc;
            DateTime expiresUtc;

            try
            {
                issuedUtc = new DateTime(issuedTicks, DateTimeKind.Utc);
                expiresUtc = new DateTime(expiresTicks, DateTimeKind.Utc);
            }
            catch
            {
                errorMessage = "OVIA Master 복구 응답의 유효시간을 확인할 수 없습니다.";
                return false;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (issuedUtc > nowUtc.AddSeconds(15) || issuedUtc < nowUtc.AddMinutes(-2))
            {
                errorMessage = "OVIA Master 복구 요청 시간이 유효하지 않습니다.";
                return false;
            }

            if (expiresUtc <= nowUtc || expiresUtc > issuedUtc.AddMinutes(2))
            {
                errorMessage = "OVIA Master 복구 서명의 유효시간이 만료되었거나 올바르지 않습니다.";
                return false;
            }

            companyId = companyId == null ? "" : companyId.Trim();
            userId = userId == null ? "" : userId.Trim();
            sessionPassword = sessionPassword == null ? "" : sessionPassword.Trim();

            if (companyId == "" || companyId.Length > MaxCompanyIdLength)
            {
                errorMessage = "OVIA Master 복구 대상 기업아이디가 올바르지 않습니다.";
                return false;
            }

            if (!string.Equals(userId, RecoveryUserId, StringComparison.Ordinal))
            {
                errorMessage = "OVIA Master 복구 사용자 식별값이 올바르지 않습니다.";
                return false;
            }

            if (sessionPassword == "" || sessionPassword.Length > MaxSessionPasswordLength)
            {
                errorMessage = "OVIA Master 복구 세션 확인암호가 올바르지 않습니다.";
                return false;
            }

            string signedText = request
                + "|" + parts[1]
                + "|" + parts[2]
                + "|" + parts[3]
                + "|" + parts[4]
                + "|" + parts[5];

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(parts[6]);
            }
            catch
            {
                errorMessage = "OVIA Master 복구 서명값이 올바르지 않습니다.";
                return false;
            }

            if (!VerifySignature(Encoding.UTF8.GetBytes(signedText), signature))
            {
                errorMessage = "OVIA Master 복구 서명 검증에 실패했습니다.";
                return false;
            }

            context = new OviaMasterRecoveryContext();
            context.CompanyId = companyId;
            context.UserId = userId;
            context.SessionPassword = sessionPassword;
            context.ExpiresUtc = expiresUtc;
            return true;
        }

        private static bool VerifySignature(byte[] data, byte[] signature)
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(PublicKeyXml);
                return rsa.VerifyData(
                    data,
                    CryptoConfig.MapNameToOID("SHA256"),
                    signature);
            }
        }
    }
}
