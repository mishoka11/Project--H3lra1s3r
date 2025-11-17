using Microsoft.AspNetCore.Mvc;

namespace H3lRa1s3r.Api.OrderService
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class OrdersController : ControllerBase
    {
        // In-memory DB (using your Models.OrdersDb)
        [HttpGet]
        public IActionResult GetAll()
        {
            return Ok(Models.OrdersDb.Orders.Values);
        }

        [HttpGet("{id}")]
        public IActionResult GetById(string id)
        {
            if (!Models.OrdersDb.Orders.TryGetValue(id, out var order))
                return NotFound();

            return Ok(order);
        }

        [HttpPost]
        public IActionResult Create([FromBody] Models.Order order)
        {
            if (order == null)
                return BadRequest("Invalid order");

            Models.OrdersDb.Orders[order.Id] = order;
            return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            if (!Models.OrdersDb.Orders.Remove(id))
                return NotFound();

            return NoContent();
        }
    }
}
