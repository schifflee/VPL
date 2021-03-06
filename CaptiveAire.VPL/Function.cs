﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CaptiveAire.VPL.Extensions;
using CaptiveAire.VPL.Interfaces;
using CaptiveAire.VPL.Metadata;
using CaptiveAire.VPL.Model;
using CaptiveAire.VPL.View;
using CaptiveAire.VPL.ViewModel;
using Cas.Common.WPF;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using Newtonsoft.Json;

namespace CaptiveAire.VPL
{
    internal class Function : ViewModelBase, IFunction
    {
        private readonly IVplServiceContext _context;
        private readonly Guid _functionId;
        private string _name;
        private readonly Elements _elements;
        private readonly ObservableCollection<IVariable> _variables = new ObservableCollection<IVariable>();
        private readonly OrderedListViewModel<IArgument> _arguments;
        private Guid? _returnTypeId;
        private bool _isDirty;
        private readonly ISelectionService _selectionService = new SelectionService();
        private readonly UndoService<string> _undoService = new UndoService<string>();

        public Function(IVplServiceContext context, Guid functionId)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            _context = context;
            _functionId = functionId;

            _arguments = new OrderedListViewModel<IArgument>(
                CreateArgument,
                deleted: DeleteArgument,
                addedAction: ArgumentAdded);

            _elements = new Elements(this);

            UndoCommand = new RelayCommand(Undo, _undoService.CanUndo);
            RedoCommand = new RelayCommand(Redo, _undoService.CanRedo);
            CopyCommand = new RelayCommand(Copy, CanCopy);
            CutCommand = new RelayCommand(Cut, CanCut);
            PasteCommand = new RelayCommand(Paste, CanPaste);
            SelectAllCommand = new RelayCommand(SelectAll);
            
        }

        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand CutCommand { get;  }
        public ICommand PasteCommand { get; }
        public ICommand SelectAllCommand { get; }

        private void SelectAll()
        {
            SelectionService.SelectNone();

            foreach (var element in Elements)
            {
                SelectionService.Select(element);
            }
        }

        private void Copy()
        {
            //Get the elements
            var elements = SelectionService.GetSelected()
                .OfType<IElement>();

            ClipboardUtility.Copy(elements);
        }

        private bool CanCopy()
        {
            return SelectionService.GetSelected().Any();
        }

        private void Cut()
        {
            //Get the elements
            var elements = SelectionService.GetSelected()
                .OfType<IElement>()
                .ToArray();

            ClipboardUtility.Copy(elements);

            foreach (var element in elements)
            {
                element?.Parent.RemoveElement(element);
            }

            SelectionService.SelectNone();
        }

        private bool CanCut()
        {
            return SelectionService.GetSelected().Any();
        }

        private void Paste()
        {
            var data = ClipboardUtility.Paste();

            if (data != null)
            {
                Drop(data, true);
            }
        }

        private bool CanPaste()
        {
            return ClipboardUtility.CanPaste();
        }

        private void Undo()
        {
            var state = _undoService.Undo();

            //Apply the state
            ApplyState(state);
        }

        private void Redo()
        {
            var state = _undoService.Redo();

            //Apply the state
            ApplyState(state);
        }

        private void ApplyState(string state)
        {
            try
            {
                var elements = JsonConvert.DeserializeObject<ElementMetadata[]>(state);

                Elements.Clear();

                Context.ElementBuilder.AddToOwner(this, elements);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), ex.Source);
            }
        }

        /// <summary>
        /// Returns a new argument if one was created, false otherwise.
        /// </summary>
        /// <returns></returns>
        private IArgument CreateArgument()
        {
            var metadata = new ArgumentMetadata()
            {
                Id = Guid.NewGuid(),
            };

            //Create the view model
            var viewModel = new ArgumentDialogViewModel(metadata, _context.Types);

            //Create the dialog
            var view = new ArgumentDialogView()
            {
                DataContext = viewModel,
                Owner = WindowUtil.GetActiveWindow()
            };

            //Show the dialog
            if (view.ShowDialog() == true)
            {
                //Create the argument
                return new Argument(this, viewModel.ToMetadata());               
            }

            return null;
        }

        private void ArgumentAdded(IArgument argument)
        {
            if (argument == null)
                return;

            //Create the variable for this argument.
            var variable = new ArgumentVariable(this, this.GetVplTypeOrThrow(argument.TypeId), argument)
            {
                Name = argument.Name
            };

            //Add the variable.
            AddVariable(variable);

            //I feel so dirty now.
            MarkDirty();
        }

        public void AddArgument(IArgument argument)
        {
            if (argument == null) throw new ArgumentNullException(nameof(argument));
            if (argument.Id == Guid.Empty)
                throw new ArgumentException($"The id of argument '{argument.Name}' was empty.");

            //Add the argument
            _arguments.Add(argument);

            //I feel so dirty now.
            MarkDirty();
        }

        private bool DeleteArgument(IArgument argument)
        {
            if (argument == null) throw new ArgumentNullException(nameof(argument));

            //Delete the corresponding variable
            var variable = Variables.FirstOrDefault(v => v.Id == argument.Id);

            if (variable != null && variable.CanDelete())
            {
                if (variable.Delete())
                {
                    Arguments.Remove(argument);
                    MarkDirty();

                    return true;
                }
            }

            return false;
        }

        public bool IsDirty
        {
            get { return _isDirty; }
            private set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    RaisePropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ISelectionService SelectionService
        {
            get { return _selectionService; }
        }

        public IEnumerable<IArgument> GetArguments()
        {
            return _arguments.ToArray();
        }

        public void MarkDirty()
        {
            IsDirty = true;
        }

        public void MarkClean()
        {
            IsDirty = false;
        }

        public bool CanDrop(IElementClipboardData data)
        {
            return this.AreAllItemsStatements(data);
        }

        public bool Drop(IElementClipboardData data, bool insertAtBeginning)
        {
            if (CanDrop(data))
            {
                var elements = this.CreateElements(data);

                var index = insertAtBeginning ? 0 : Elements.Count;

                foreach (var element in elements)
                {
                    Elements.Insert(index++, element);
                }

                MarkDirty();

                SaveUndoState();

                //Make sure that the new items are selected.
                SelectionService.SelectNone();

                foreach (var selectable in elements)
                {
                    SelectionService.Select(selectable);
                }

                return true;
            }

            return false;
        }

        public void Add(IElement element)
        {
            if (element != null && !_elements.Contains(element))
            {
                _elements.Add(element);

                MarkDirty();
            }
        }

        public void Remove(IElement element)
        {
            if (element != null)
            {
                Elements.Remove(element);

                MarkDirty();
            }
        }

        public IEnumerable<IVariable> Variables
        {
            get { return _variables; }
        }

        public void AddVariable(IVariable variable)
        {
            _variables.Add(variable);

            MarkDirty();
        }

        public void RemoveVariable(IVariable variable)
        {
            _variables.Remove(variable);

            MarkDirty();
        }

        public IElements Elements
        {
            get { return _elements; }
        }

        public Guid Id
        {
            get { return _functionId; }
        }

        public string Name
        {
            get { return _name; }
            set
            {
                _name = value; 
                RaisePropertyChanged();
                MarkDirty();
            }
        }

        public Guid? ReturnTypeId
        {
            get { return _returnTypeId; }
            set
            {
                _returnTypeId = value; 
                RaisePropertyChanged();
                MarkDirty();
            }
        }

        public async Task<object> ExecuteAsync(object[] parameters, IExecutionContext executionContext, CancellationToken cancellationToken)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            cancellationToken.ThrowIfCancellationRequested();

            //Make sure that the # of parameters match.
            if (parameters.Length != Arguments.Count)
            {
                throw new ArgumentMismatchException($"The function '{Name}' contains {Arguments.Count} arguments but {parameters.Length} were specified.");
            }

            int argumentIndex = 0;

            //Copy the parameter values to the variable values
            foreach (var argument in Arguments)
            {
                //Look for the variable with the same id.
                var variable = Variables.FirstOrDefault(v => v.Id == argument.Id);

                //Check to make sure we found it.
                if (variable == null)
                {
                    throw new ArgumentException($"Argument variable missing for argument '{argument.Name}' in function '{Name}'.");                   
                }

                //Copy the value
                variable.Value = parameters[argumentIndex];

                argumentIndex++;
            }

            //Clear all of the errors
            Elements.ForAll(e =>
            {
                var errorSource = e as IErrorSource;

                errorSource?.ClearErrors();
            });

            //Execute the function.
            await executionContext.ExecuteStatementsAsync(Elements, cancellationToken);

            //Find the return variable
            var returnVariable = Variables.FirstOrDefault(v => v.Id == ReturnValueVariable.ReturnVariableId);

            //return the returnVariable value (or null)
            return returnVariable?.Value;
        }

        public void ClearErrors()
        {
            //Clear anything that implments IErrorSource.
            Elements.ForAll(e => (e as IErrorSource)?.ClearErrors());
        }

        public void SetError(string message)
        {
        }

        public IError[] CheckForErrors()
        {
            //Create a place to put the errors
            var errors = new List<IError>();

            Elements.ForAll(e =>
            {
                //Check to see if it's an error source.
                var errorSource = e as IErrorSource;

                //Check for errors
                var childErrors = errorSource?.CheckForErrors();

                //Check to see if there were any errors.
                if (childErrors != null && childErrors.Length > 0)
                {
                    //Add the errors to the list.
                    errors.AddRange(childErrors);
                }
            });

            return errors.ToArray();
        }

        public bool HasError
        {
            get { return false; }
        }

        public IVplServiceContext Context
        {
            get { return _context; }
        }

        public OrderedListViewModel<IArgument> Arguments
        {
            get { return _arguments; }
        }

        private IUndoService<string> UndoService
        {
            get { return _undoService; }
        }

        public IEnumerable<IElement> GetAllElements()
        {
            return Elements.EnumerateAllElements();
        }

        public IEnumerable<IElement> GetRootElements()
        {
            return Elements
                .ToArray();
        }

        public void SaveUndoState()
        {
            //Get the metadata
            var metadata = Elements.ToMetadata();

            //Serialize it
            var json = JsonConvert.SerializeObject(metadata, Formatting.Indented);

            //Save the action
            UndoService.Do(json);
        }
    }
}