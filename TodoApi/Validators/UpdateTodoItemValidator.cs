using FluentValidation;
using TodoApi.Dtos;

namespace TodoApi.Validators;

public class UpdateTodoItemValidator : AbstractValidator<UpdateTodoItem>
{
    public UpdateTodoItemValidator()
    {
        RuleFor(r => r.Description)
            .NotEmpty()
            .WithMessage("missing_name");
    }
}
