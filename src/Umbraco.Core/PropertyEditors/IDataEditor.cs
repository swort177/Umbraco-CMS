﻿using System.Collections.Generic;
using Umbraco.Core.Composing;

namespace Umbraco.Core.PropertyEditors
{
    /// <summary>
    /// Represents a data editor.
    /// </summary>
    /// <remarks>This is the base interface for parameter and property editors.</remarks>
    public interface IDataEditor : IDiscoverable
    {
        /// <summary>
        /// Gets the alias of the editor.
        /// </summary>
        string Alias { get; }

        /// <summary>
        /// Gets the type of the editor.
        /// </summary>
        /// <remarks>An editor can be a property value editor, or a parameter editor.</remarks>
        EditorType Type { get; }

        /// <summary>
        /// Gets the name of the editor.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the icon of the editor.
        /// </summary>
        /// <remarks>Can be used to display editors when presenting them.</remarks>
        string Icon { get; }

        /// <summary>
        /// Gets the group of the editor.
        /// </summary>
        /// <remarks>Can be used to organize editors when presenting them.</remarks>
        string Group { get; }

        /// <summary>
        /// Gets the value editor.
        /// </summary>
        IDataValueEditor ValueEditor { get; } // fixme should be a method - but, deserialization?

        /// <summary>
        /// Gets the configuration for the value editor.
        /// </summary>
        IDictionary<string, object> DefaultConfiguration { get; }
    }
}