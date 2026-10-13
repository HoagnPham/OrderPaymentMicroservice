using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderPaymentMicroservice.Operations;

namespace OrderPaymentMicroservice.Controllers;

[ApiController, Route("api/operations"), Authorize(Roles="Admin")]
public sealed class OperationsController(OperationsSnapshot snapshot, ILogger<OperationsController> logger):ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken token)
    {
        Response.Headers.CacheControl="no-store";
        try{return Ok(await snapshot.Read(token));}
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch{logger.LogWarning("Operations snapshot is unavailable");return StatusCode(503,new{message="Operational snapshot unavailable"});}
    }
}
