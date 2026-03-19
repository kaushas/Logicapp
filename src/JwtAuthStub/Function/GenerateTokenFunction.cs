using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace JwtAuthStub.Function
{
    public static class GenerateTokenFunction
    {
        [FunctionName("GenerateToken")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "generate-token")] HttpRequest req,
            ILogger log)
        {
            // Simple symmetric JWT for stub purposes. In production use secure signing and short TTL.
            var key = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY") ?? "dev_signing_key_please_replace";
            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim("sub", "logicapp-svc") }),
                Expires = DateTime.UtcNow.AddMinutes(30),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var jwt = tokenHandler.WriteToken(token);

            return new OkObjectResult(new { token = jwt });
        }
    }
}
