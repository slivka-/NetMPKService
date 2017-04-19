using System;
using System.Security.Cryptography;
using System.Text;
using System.Configuration;

namespace NetMPK.Service
{
    public static class Encryptor 
    {
        public static string Encrypt(string input)
        {
            byte[] encKey = Encoding.UTF8.GetBytes(ConfigurationManager.AppSettings["encKey"].ToString());
            byte[] incVec = Encoding.UTF8.GetBytes(ConfigurationManager.AppSettings["incVec"].ToString());
            while (input.Length % 4 != 0)
                input += "q";
            SymmetricAlgorithm alg = DES.Create();
            ICryptoTransform trans = alg.CreateEncryptor(encKey, incVec);
            byte[] inputBuffer = Convert.FromBase64String(input);
            byte[] outputBuffer = trans.TransformFinalBlock(inputBuffer, 0, inputBuffer.Length);
            return Encoding.ASCII.GetString(outputBuffer);
        }
    }
}