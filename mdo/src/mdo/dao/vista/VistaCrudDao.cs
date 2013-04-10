﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using gov.va.medora.mdo.exceptions;
using gov.va.medora.utils;

namespace gov.va.medora.mdo.dao.vista
{
    public class VistaCrudDao
    {
        VistaConnection _cxn;

        public VistaCrudDao(AbstractConnection cxn)
        {
            _cxn = (VistaConnection)cxn;
        }

        #region Delete

        public void delete(String recordIen, String vistaFile)
        {
            DdrFiler request = buildDeleteRequest(recordIen, vistaFile);
            String response = request.execute();
            toCreateUpdateDeleteRecordResponse(response);
        }

        /// <summary>
        /// Query to delete a record from a file
        /// </summary>
        /// <param name="recordIen">The entry IEN. Can be a subfile (IENS string needs to be built correctly)</param>
        /// <param name="vistaFile">The Vista file from which to delete a record</param>
        /// <returns></returns>
        internal DdrFiler buildDeleteRequest(String recordIen, String vistaFile)
        {
            DdrFiler query = new DdrFiler(_cxn);
            query.Operation = "EDIT";
            query.Args = new string[]
            {
                vistaFile + "^.01^" + recordIen + "^@" // per API docs, setting .01 field to "@" deletes record
            };
            return query;

        }

        #endregion

        #region Create

        /// <summary>
        /// Create a new record entry in a Vista file
        /// </summary>
        /// <param name="fieldsAndValues">The field number and value dictionary</param>
        /// <param name="vistaFile">The Vista file number</param>
        /// <param name="iens">If creating a record in a subfile, the IENS string of the parent record</param>
        /// <returns>The IEN of the new record</returns>
        public String create(Dictionary<String, String> fieldsAndValues, String vistaFile, String iens = null)
        {
            Dictionary<String, String> wpFieldsAndValues = findWpFields(fieldsAndValues);
            DdrFiler request = buildCreateRequest(fieldsAndValues, vistaFile, iens);
            String response = request.execute();
            String result = toCreateUpdateDeleteRecordResponse(response);
            // if we get this far, create succeeded! to make the API easier to use, enable user to pass WP fields in dictionary
            foreach (String key in wpFieldsAndValues.Keys)
            {
                addWordProcessing(vistaFile, key, String.Concat(result, ",", iens), wpFieldsAndValues[key]);
            }
            return result;
        }

        internal Dictionary<string, string> findWpFields(Dictionary<string, string> fieldsAndValues)
        {
            Dictionary<String, String> result = new Dictionary<String, String>();
            foreach (String key in fieldsAndValues.Keys)
            {
                if (key.Contains("WP"))
                {
                    result.Add(key, fieldsAndValues[key]);
                }
            }
            foreach (String key in result.Keys) // now remove all wp fields because we don't want to include those in the vanilla create/update
            {
                fieldsAndValues.Remove(key);
            }
            return result;
        }

        public void addWordProcessing(String vistaFile, String vistaField, String iens, String wpText)
        {
            DdrWpFiler request = buildAddWpRequest(vistaFile, vistaField, iens, wpText);
            String response = request.execute();
            toCreateUpdateDeleteRecordResponse(response);
        }

        internal String toCreateUpdateDeleteRecordResponse(string response)
        {
            if (String.IsNullOrEmpty(response))
            {
                throw new MdoException("An empty response was received but is invalid for this operation");
            }

            String[] pieces = StringUtils.split(response, StringUtils.CRLF);

            if (pieces.Length > 1 && pieces[1].Contains("BEGIN_diERRORS"))
            {
                throw new MdoException(response);
            }

            if (pieces[0].Contains("[Data]") && pieces.Length > 1) //sample create valid response: "[Data]\r\n+1,^2\r\n" <- 2 is IEN for new record
            {
                Int32 startIdx = pieces[1].IndexOf('^');
                return startIdx > 0 ? pieces[1].Substring(startIdx + 1) : "";
            }
            else // "[Data]" response means everything was ok
            {
                return "OK";
            }
        }

        internal DdrFiler buildCreateRequest(Dictionary<String, String> fieldsAndValues, String vistaFile, String iens = null)
        {
            DdrFiler ddr = new DdrFiler(_cxn);
            ddr.Operation = "ADD";

            int index = 0;
            ddr.Args = new String[fieldsAndValues.Count];
            foreach (String key in fieldsAndValues.Keys)
            {
                if (String.IsNullOrEmpty(iens))
                {
                    ddr.Args[index++] = vistaFile + "^" + key + "^+1,^" + fieldsAndValues[key]; // e.g. [0]: 2^.01^+1,PATIENT,NEW^DDROOT(1)  [1]: 2^.09^+1,^222113333
                }
                else
                {
                    ddr.Args[index++] = vistaFile + "^" + key + "^+1," + iens + "^" + fieldsAndValues[key]; // e.g. [0]: 2^.01^+1,PATIENT,NEW  [1]: 2^.09^+1,^222113333
                }
            }

            return ddr;
        }

        internal DdrWpFiler buildAddWpRequest(String vistaFile, String field, String iens, String wpText)
        {
            String[] lines = StringUtils.split(wpText, StringUtils.CRLF);

            DdrWpFiler ddr = new DdrWpFiler(_cxn);
            ddr.Operation = "EDIT"; // both "ADD" and "EDIT" seem to work just fine
            ddr.Params = new DictionaryHashList();
            if (field.Contains("WP")) // if this was called from create or update, it probably contains "WP" to denote this as a special field so we should remove that
            {
                field = field.Replace("WP", "");
            }
            ddr.Params.Add("1", vistaFile + "^" + field + "^" + iens + "^DDRROOT(1)"); // taken from FileMan Delphi Components pascal code
            for (int i = 0; i < lines.Length; i++)
            {
                ddr.Params.Add("1," + (i + 1).ToString(), lines[i]); // taken from FileMan Delphi Components pascal code
            }

            return ddr;
        }

        #endregion

        #region Read

        /// <summary>
        /// Returns a dictionary of field numbers and values
        /// </summary>
        /// <param name="recordIen">The IEN in the Vista file</param>
        /// <param name="fields">Separate fields with a semicolon - e.g.: .01;.02;9  Leave blank to retrieve all fields</param>
        /// <param name="vistaFile">The Vista file number</param>
        /// <returns>Dictionary<String, String></returns>
        public Dictionary<String, String> read(String recordIen, String fields, String vistaFile, String flags = null)
        {
            DdrGetsEntry ddr = buildReadRequest(recordIen, fields, vistaFile);
            String[] results = ddr.execute();
            return ddr.convertToFieldValueDictionary(results);
        }

        internal DdrGetsEntry buildReadRequest(String recordIen, String fields, String vistaFile, String flags = null)
        {
            DdrGetsEntry ddr = new DdrGetsEntry(_cxn);
            ddr.Fields = String.IsNullOrEmpty(fields) ? "*" : fields;
            ddr.File = vistaFile;
            ddr.Flags = String.IsNullOrEmpty(flags) ? "IN" : flags;
            ddr.Iens = recordIen.EndsWith(",") ? recordIen : String.Concat(recordIen, ","); // helper to add trailing comma if not present
            return ddr;
        }

        #endregion

        #region Update

        public void update(Dictionary<String, String> fieldsAndValues, String ien, String vistaFile)
        {
            Dictionary<String, String> wpFieldsAndValues = findWpFields(fieldsAndValues);
            if (fieldsAndValues.Count > 0) // need to check this in case we were only updating WP fields
            {
                DdrFiler request = buildUpdateRequest(fieldsAndValues, ien, vistaFile);
                String response = request.execute();
                toCreateUpdateDeleteRecordResponse(response); // should throw exception on failure
            }
            // if we get this far, create succeeded! to make the API easier to use, enable user to pass WP fields in dictionary
            foreach (String key in wpFieldsAndValues.Keys)
            {
                addWordProcessing(vistaFile, key, ien, wpFieldsAndValues[key]);
                fieldsAndValues.Add(key, wpFieldsAndValues[key]); // want to add this back to original dict so that we don't permanently change it's state
            }
        }

        internal DdrFiler buildUpdateRequest(Dictionary<String, String> fieldsAndValues, String ien, String vistaFile)
        {
            DdrFiler ddr = new DdrFiler(_cxn);
            ddr.Operation = "UPDATE";

            int index = 0;
            ddr.Args = new String[fieldsAndValues.Count];
            foreach (String key in fieldsAndValues.Keys)
            {
                ddr.Args[index++] = vistaFile + "^" + key + "^" + ien + "^" + fieldsAndValues[key]; // e.g. [0]: 2^.01^5,^PATIENT,NEW  [1]: 2^.09^5,^222113333
            }

            return ddr;
        }


        #endregion
    }
}
