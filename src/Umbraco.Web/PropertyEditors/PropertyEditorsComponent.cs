﻿using System.Linq;
using Umbraco.Core;
using Umbraco.Core.Components;
using Umbraco.Core.Models;
using Umbraco.Core.PropertyEditors;
using Umbraco.Core.Services;
using UmbracoExamine;

namespace Umbraco.Web.PropertyEditors
{
    [RuntimeLevel(MinLevel = RuntimeLevel.Run)]
    internal class PropertyEditorsComponent : UmbracoComponentBase, IUmbracoCoreComponent
    {
        public void Initialize(IRuntimeState runtime, PropertyEditorCollection propertyEditors, IExamineIndexCollectionAccessor indexCollection)
        {
            if (runtime.Level != RuntimeLevel.Run) return;

            var fileUpload = propertyEditors.OfType<FileUploadPropertyEditor>().FirstOrDefault();
            if (fileUpload != null) Initialize(fileUpload);

            var imageCropper = propertyEditors.OfType<ImageCropperPropertyEditor>().FirstOrDefault();
            if (imageCropper != null) Initialize(imageCropper);

            var grid = propertyEditors.OfType<GridPropertyEditor>().FirstOrDefault();
            if (grid != null) Initialize(grid, indexCollection);
        }

        // as long as these methods are private+static they won't be executed by the boot loader

        private static void Initialize(FileUploadPropertyEditor fileUpload)
        {
            MediaService.Saving += fileUpload.MediaServiceSaving;
            MediaService.Created += fileUpload.MediaServiceCreating;
            ContentService.Copied += fileUpload.ContentServiceCopied;

            MediaService.Deleted += (sender, args) 
                => args.MediaFilesToDelete.AddRange(fileUpload.ServiceDeleted(args.DeletedEntities.Cast<ContentBase>()));
            MediaService.EmptiedRecycleBin += (sender, args) 
                => args.Files.AddRange(fileUpload.ServiceEmptiedRecycleBin(args.AllPropertyData));
            ContentService.Deleted += (sender, args) 
                => args.MediaFilesToDelete.AddRange(fileUpload.ServiceDeleted(args.DeletedEntities.Cast<ContentBase>()));
            ContentService.EmptiedRecycleBin += (sender, args) 
                => args.Files.AddRange(fileUpload.ServiceEmptiedRecycleBin(args.AllPropertyData));
            MemberService.Deleted += (sender, args) 
                => args.MediaFilesToDelete.AddRange(fileUpload.ServiceDeleted(args.DeletedEntities.Cast<ContentBase>()));
        }

        private static void Initialize(ImageCropperPropertyEditor imageCropper)
        {
            MediaService.Saving += imageCropper.MediaServiceSaving;
            MediaService.Created += imageCropper.MediaServiceCreated;
            ContentService.Copied += imageCropper.ContentServiceCopied;

            MediaService.Deleted += (sender, args) 
                => args.MediaFilesToDelete.AddRange(imageCropper.ServiceDeleted(args.DeletedEntities.Cast<ContentBase>()));
            MediaService.EmptiedRecycleBin += (sender, args) 
                => args.Files.AddRange(imageCropper.ServiceEmptiedRecycleBin(args.AllPropertyData));
            ContentService.Deleted += (sender, args) 
                => args.MediaFilesToDelete.AddRange(imageCropper.ServiceDeleted(args.DeletedEntities.Cast<ContentBase>()));
            ContentService.EmptiedRecycleBin += (sender, args) 
                => args.Files.AddRange(imageCropper.ServiceEmptiedRecycleBin(args.AllPropertyData));
            MemberService.Deleted += (sender, args) 
                => args.MediaFilesToDelete.AddRange(imageCropper.ServiceDeleted(args.DeletedEntities.Cast<ContentBase>()));
        }

        private static void Initialize(GridPropertyEditor grid, IExamineIndexCollectionAccessor indexCollection)
        {
            var indexes = indexCollection.Indexes;
            if (indexes == null) return;
            foreach (var i in indexes.Values.OfType<BaseUmbracoIndexer>())
                i.DocumentWriting += grid.DocumentWriting;
        }
    }
}