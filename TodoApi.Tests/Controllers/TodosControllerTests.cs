using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Controllers;
using TodoApi.Data;
using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Tests.Controllers;

public class TodosControllerTests
{
    private long _validTodoListId;
    private long _validTodoItemId;
    
    private static DbContextOptions<TodoContext> DatabaseContextOptions()
    {
        return new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private void PopulateDatabaseContext(TodoContext context)
    {
        var todoList = new TodoList { Id = 1, Name = "Task 1", TodoItems = [
            new TodoItem
            {
                Description = "Demo 1",
                Completed = false,
            },
        ]};
        
        context.TodoList.Add(todoList);
        
        context.SaveChanges();
        
        _validTodoListId = todoList.Id;
        _validTodoItemId = todoList.TodoItems.First().Id;
    }
    
    [Fact]
    public async Task CreateTodoItem_WhenCalled_CreatesTodoItem()
    {
        // Arrange.
        await using var context = new TodoContext(DatabaseContextOptions());
        
        PopulateDatabaseContext(context);

        var controller = new TodosController(context);
   
        // Act.
        var createResult = await controller.CreateTodoItem(
            _validTodoListId,
            new CreateTodoItem("Todo Item 1", false)
        );
        
        // Assert.
        Assert.IsType<OkObjectResult>(createResult.Result);
        var todoItem = ((OkObjectResult)createResult.Result).Value as CreateTodoItemResult;
        Assert.NotNull(todoItem);
        Assert.True(todoItem.id > 0);
        Assert.NotEmpty(todoItem.description);
    }
    
    [Fact]
    public async Task CreateTodoItem_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange.
        await using var context = new TodoContext(DatabaseContextOptions());
        var controller = new TodosController(context);
        
        PopulateDatabaseContext(context);
        
        // Act.
        var createResult = await controller.CreateTodoItem(
            _validTodoListId,
            new CreateTodoItem("", false)
        );
        
        // Assert.
        Assert.IsType<BadRequestObjectResult>(createResult.Result);
        var errors = (((BadRequestObjectResult)createResult.Result).Value as IEnumerable<ValidationFailure>)!.ToList();
        Assert.NotNull(errors);
        Assert.NotEmpty(errors);
        Assert.True(errors.Any(e => e is { PropertyName: "Description", ErrorMessage: "missing_name" }));
    }
    
    [Fact]
    public async Task CreateTodoItem_WhenCalled_ReturnsSanitizedTodoItem()
    {
        // Arrange.
        await using var context = new TodoContext(DatabaseContextOptions());
        
        PopulateDatabaseContext(context);

        var controller = new TodosController(context);
   
        // Act.
        var createResult = await controller.CreateTodoItem(
            _validTodoListId,
            new CreateTodoItem("<script>alert('XSS')</script> Demo 1", false)
        );
        
        // Assert.
        Assert.IsType<OkObjectResult>(createResult.Result);
        var todoItem = ((OkObjectResult)createResult.Result).Value as CreateTodoItemResult;
        Assert.NotNull(todoItem);
        Assert.True(todoItem.id > 0);
        Assert.Equal(" Demo 1", todoItem.description);
    }
    
    [Fact]
    public async Task UpdateTodoItem_WithValidData_UpdatesTodoItem()
    {
        // Arrange.
        await using var context = new TodoContext(DatabaseContextOptions());
        var controller = new TodosController(context);
        
        PopulateDatabaseContext(context);
        
        // Act.
        var updateResult = await controller.UpdateTodoItem(_validTodoListId, _validTodoItemId,
            new UpdateTodoItem("Demo 2", true));
        
        // Assert.
        Assert.IsType<OkObjectResult>(updateResult.Result);
        var todoItem = ((OkObjectResult)updateResult.Result).Value as UpdateTodoItemResult;
        Assert.NotNull(todoItem);
        Assert.True(todoItem.completed);
        Assert.Equal("Demo 2", todoItem.description);
    }
}
