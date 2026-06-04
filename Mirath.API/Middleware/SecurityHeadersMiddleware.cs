namespace Mirath.API.Middleware;

/// <summary>
/// Adds security headers to every HTTP response.
/// CSP includes Anthropic API for direct-browser AI calls (standalone mode).
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var h = context.Response.Headers;

        h["X-Content-Type-Options"]             = "nosniff";
        h["X-Frame-Options"]                    = "DENY";
        h["X-XSS-Protection"]                   = "1; mode=block";
        h["Referrer-Policy"]                    = "strict-origin-when-cross-origin";
        h["Permissions-Policy"]                 = "geolocation=(), microphone=(), camera=(), payment=(), usb=()";
        h["X-Permitted-Cross-Domain-Policies"]  = "none";
        h["Cross-Origin-Opener-Policy"]         = "same-origin";
        h["Cross-Origin-Resource-Policy"]       = "cross-origin";

        // CSP — connect-src includes Anthropic for standalone AI features
        var csp = string.Join("; ",
            "default-src 'self'",
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net",
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
            "font-src 'self' https://fonts.gstatic.com data:",
            "img-src 'self' data: https: blob:",
            "connect-src 'self' https://api.anthropic.com https://api.openai.com wss: ws:",
            "worker-src 'self' blob:",
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "upgrade-insecure-requests"
        );
        h["Content-Security-Policy"] = csp;

        // HSTS — 2 years required for preload list
        if (context.Request.IsHttps)
            h["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains; preload";

        await next(context);
    }
}
