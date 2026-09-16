using System;
using System.Security.Cryptography;
using System.Text;

namespace TavernLib.Backend;

public static class BackendUtils
{
	public static string TavernApi => "http://themoddingtavern.com:1763";

	public static string ServerUri => "/servers";

	public static string HashDigest(string input)
	{
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(input));
		return BitConverter.ToString(array).Replace("-", "").ToLower();
	}

	public static string TokenUrlSafe(int nbytes = 32)
	{
		byte[] array = new byte[nbytes];
		using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
		{
			randomNumberGenerator.GetBytes(array);
		}
		return Convert.ToBase64String(array).Replace('+', '-').Replace('/', '_')
			.TrimEnd('=');
	}
}
