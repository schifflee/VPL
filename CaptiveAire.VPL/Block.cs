﻿using System;
using System.Threading;
using System.Threading.Tasks;
using CaptiveAire.VPL.Interfaces;
using CaptiveAire.VPL.Model;

namespace CaptiveAire.VPL
{
    internal class Block : Element, IBlock, IElementDropTarget
    {
        private readonly string _id;
        private bool _isEnabled = true;
        private bool _isDraggingOver;

        private readonly Elements _elements = new Elements();

        public Block(IElementCreationContext context, string id) 
            : base(context)
        {
            _id = id;
        }

        public void Drop(IStatement dropped)
        {
            
        }

        public bool CanDrop()
        {
            return true;
        }

        public bool IsDraggingOver
        {
            get { return _isDraggingOver; }
            set
            {
                _isDraggingOver = value;
                RaisePropertyChanged();
            }
        }

        public void Drop(IElement element)
        {
            var statement = element as IStatement;

            if (statement != null)
            {
                Elements.Insert(0, element);
            }
        }

        public bool CanDrop(Type elementType, Guid? returnType)
        {
            return elementType != null && typeof(IStatement).IsAssignableFrom(elementType);
        }


        public async Task ExecuteAsync(IExecutionContext executionContext, CancellationToken token)
        {
            var executor = new StatementExecutor();

            await executor.ExecuteAsync(executionContext, Elements, token);
        }

        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                _isEnabled = value; 
                RaisePropertyChanged();
            }
        }

        public string Name
        {
            get { return "Block"; }
        }

        public string Id
        {
            get { return _id; }
        }

        public IElements Elements
        {
            get { return _elements; }
        }
    }
}