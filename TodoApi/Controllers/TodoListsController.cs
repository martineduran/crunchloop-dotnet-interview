using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Services;

namespace TodoApi.Controllers
{
    [Route("api/todolists")]
    [ApiController]
    public class TodoListsController : ControllerBase
    {
        private readonly TodoContext _context;
        private readonly IBackgroundJobQueue _jobQueue;

        public TodoListsController(TodoContext context, IBackgroundJobQueue jobQueue)
        {
            _context = context;
            _jobQueue = jobQueue;
        }

        // GET: api/todolists
        [HttpGet]
        public async Task<ActionResult<IList<TodoListDto>>> GetTodoLists()
        {
            var todoLists = await _context.TodoList
                .Select(tl => new TodoListDto
                {
                    Id = tl.Id,
                    Name = tl.Name,
                    IncompleteItemCount = tl.TodoItems.Count(ti => !ti.Completed),
                })
                .ToListAsync();

            return Ok(todoLists);
        }

        // GET: api/todolists/5
        [HttpGet("{id:long}")]
        public async Task<ActionResult<TodoList>> GetTodoList(long id)
        {
            var todoList = await _context.TodoList.FindAsync(id);

            if (todoList == null)
            {
                return NotFound();
            }

            return Ok(todoList);
        }

        // PUT: api/todolists/5
        // To protect from over-posting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id:long}")]
        public async Task<ActionResult> PutTodoList(long id, UpdateTodoList payload)
        {
            var todoList = await _context.TodoList.FindAsync(id);

            if (todoList == null)
            {
                return NotFound();
            }

            todoList.Name = payload.Name;
            await _context.SaveChangesAsync();

            return Ok(todoList);
        }

        // POST: api/todolists
        // To protect from over-posting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<TodoList>> PostTodoList(CreateTodoList payload)
        {
            var todoList = new TodoList { Name = payload.Name, TodoItems = [] };

            _context.TodoList.Add(todoList);
            await _context.SaveChangesAsync();

            return Ok(new CreateTodoListResult(todoList.Id));
        }

        // DELETE: api/todolists/5
        [HttpDelete("{id:long}")]
        public async Task<ActionResult> DeleteTodoList(long id)
        {
            var todoList = await _context.TodoList
                .Include(tl => tl.TodoItems)
                .FirstOrDefaultAsync(tl => tl.Id == id);

            if (todoList == null)
            {
                return NotFound();
            }

            // Create tombstone if entity was synced to remote
            if (!string.IsNullOrEmpty(todoList.RemoteId))
            {
                var tombstone = new DeletedEntity
                {
                    RemoteId = todoList.RemoteId,
                    EntityType = "TodoList",
                    DeletedAt = DateTime.UtcNow,
                };
                _context.DeletedEntities.Add(tombstone);

                // Create tombstones for all items in the list
                foreach (var item in todoList.TodoItems)
                {
                    if (!string.IsNullOrEmpty(item.RemoteId))
                    {
                        var itemTombstone = new DeletedEntity
                        {
                            RemoteId = item.RemoteId,
                            EntityType = "TodoItem",
                            DeletedAt = DateTime.UtcNow,
                            ParentRemoteId = todoList.RemoteId,
                        };
                        _context.DeletedEntities.Add(itemTombstone);
                    }
                }
            }

            _context.TodoList.Remove(todoList);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // POST: api/todolists/5/complete-all
        [HttpPost("{id:long}/complete-all")]
        public async Task<ActionResult<CompleteAllTodosResult>> CompleteAllTodos(long id)
        {
            var todoList = await _context.TodoList.FindAsync(id);
            if (todoList == null)
            {
                return NotFound();
            }

            var jobId = Guid.NewGuid().ToString();
            var job = new CompleteAllTodosJob { JobId = jobId, TodoListId = id };

            _jobQueue.QueueJob(job);

            return Accepted(new CompleteAllTodosResult(jobId));
        }

        private bool TodoListExists(long id)
        {
            return (_context.TodoList?.Any(e => e.Id == id)).GetValueOrDefault();
        }
    }
}
