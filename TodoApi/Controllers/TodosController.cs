using Ganss.Xss;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Validators;

namespace TodoApi.Controllers;

[Route("api/todolists/{todoListId:long}/todos")]
public class TodosController : ControllerBase
{
    private readonly TodoContext _context;

    public TodosController(TodoContext context)
    {
        _context = context;
    }

    [HttpPost("")]
    public async Task<ActionResult<CreateTodoItemResult>> CreateTodoItem(long todoListId, [FromBody] CreateTodoItem payload)
    {
        #if DEBUG
        
        // Only to test GlobalErrorHandling.
        if (todoListId < 0)
        {
            throw new InvalidOperationException("Invalid TodoListId");
        }
        
        #endif
        
        var validationResult = new CreateTodoItemValidator().Validate(payload);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }
        
        var todoList = await _context.TodoList.FirstOrDefaultAsync(lst => lst.Id == todoListId);
        if (todoList == null)
        {
            return NotFound();
        }

        var sanitizer = new HtmlSanitizer();
        
        var todoItem = new TodoItem
        {
            Description = sanitizer.Sanitize(payload.Description), 
            Completed = payload.Completed,
        };

        try
        {
            todoList.TodoItems.Add(todoItem);
            await _context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }

        return Ok(new CreateTodoItemResult(todoItem.Id, todoItem.Description));
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<UpdateTodoItemResult>> UpdateTodoItem(long todoListId, long id, [FromBody] UpdateTodoItem payload)
    {
        var validationResult = new UpdateTodoItemValidator().Validate(payload);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }
        
        var todoList = await _context.TodoList
            .Include(tl => tl.TodoItems)
            .FirstOrDefaultAsync(tl => tl.Id == todoListId);
        if (todoList == null)
        {
            return NotFound();
        }

        var todoItem = todoList.TodoItems.FirstOrDefault(ti => ti.Id == id);
        if (todoItem == null)
        {
            return NotFound();
        }
        
        var sanitizer = new HtmlSanitizer();
        
        todoItem.Update(sanitizer.Sanitize(payload.Description), payload.Completed);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }

        return Ok(new UpdateTodoItemResult(todoItem.Description, todoItem.Completed));
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult> DeleteTodoItem(long todoListId, long id)
    {
        var todoList = await _context.TodoList
            .Include(tl => tl.TodoItems)
            .FirstOrDefaultAsync(tl => tl.Id == todoListId);
        if (todoList == null)
        {
            return NotFound();
        }

        var todoItem = todoList.TodoItems.FirstOrDefault(ti => ti.Id == id);
        if (todoItem == null)
        {
            return NotFound();
        }

        todoList.TodoItems.Remove(todoItem);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }

        return NoContent();
    }

    [HttpGet("")]
    public async Task<ActionResult<List<TodoItem>>> GetTodoItems(long todoListId)
    {
        var todoList = await _context.TodoList
            .Include(tl => tl.TodoItems)
            .FirstOrDefaultAsync(tl => tl.Id == todoListId);
        if (todoList == null)
        {
            return NotFound();
        }

        return Ok(todoList.TodoItems);
    }
}
