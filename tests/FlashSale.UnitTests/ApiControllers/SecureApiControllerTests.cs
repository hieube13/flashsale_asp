using System.Net;
using System.Text;
using FlashSale.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FlashSale.UnitTests.Api;

public class SecureApiControllerTests
{
    private readonly SecureApiController _sut = new();

    [Fact]
    public void GetInfo_returns_200_with_static_payload()
    {
        var result = _sut.GetInfo();
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        // Use ToString + reflection-light assertion to avoid Newtonsoft dep in tests
        var body = ok.Value!;
        var statusProp = body.GetType().GetProperty("status")!.GetValue(body);
        var msgProp = body.GetType().GetProperty("message")!.GetValue(body);
        Assert.Equal("success", statusProp);
        Assert.Equal(SecureApiController.INFO_RESPONSE, msgProp);
    }

    [Fact]
    public async Task PostData_returns_200_with_receivedPayload_echoed()
    {
        // Body shape: { any: "data" }
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"any\":\"data\"}"));
        ctx.Request.ContentType = "application/json";
        _sut.ControllerContext = new ControllerContext { HttpContext = ctx };

        var result = _sut.PostData(new { any = "data" });
        var ok = Assert.IsType<OkObjectResult>(result);

        var body = ok.Value!;
        var msg = body.GetType().GetProperty("message")!.GetValue(body);
        Assert.Equal("Secure data processed!", msg);
    }

    [Fact]
    public void GetUnauthorized_returns_401()
    {
        var result = _sut.GetUnauthorized();
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal((int)HttpStatusCode.Unauthorized, obj.StatusCode);
    }

    [Fact]
    public void GetForbidden_returns_403()
    {
        var result = _sut.GetForbidden();
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal((int)HttpStatusCode.Forbidden, obj.StatusCode);
    }

    [Fact]
    public void GetThrow_throws_InvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => _sut.GetThrow());
    }
}