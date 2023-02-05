/** -------------------------------------------------------------------------------------------------------------------------------
* Class Name : CLOS_LeadPushCRMNext_CC
* Description : CLOS Lead 
* Created By : Naincy
* Created On : 27.08.2021
* Modification Log:
* ----------------------------------------------------------------------------------------------------------------------------------
*/
public with sharing  class CLOS_LeadPushCRMNext_CC  {

    static final String API_ERROR = 'API Error';
    static final String REQUEST_ERROR = 'Request Error';
    static final String LEAD_CRMNEXT = 'lead_crmnext';
    static final String ERRORCODE = 'ErrorCode';
    static final String SUCCESS = 'Success';
    static final String MESSAGE = 'Message';
    static final String CRMNEXT = 'CRMNEXT';
    static final String CRMNEXT_STATUS = 'crmnext_status';
    static final String METHOD = 'POST';
    static final String AES256 = 'AES256';
    static final String CRMLEADNUM = 'CRMLeadNum';
    static final String STATUS = 'Status';
    static final String INDIVIDUAL = 'Individual';
    static final String XMLTAG = '<?xml version="1.0" encoding="UTF-8"?>';

    /**---------------------------------------------------------------------------------------------
    @description : callout function generic for all callout
	-------------------------------------------------------------------------------------------------*/
    public static HTTPResponse callOutMethod( String custom_setting,String request, String APIKey,Blob key,String requestedXML){
        
        Blob data = Blob.valueOf(requestedXML);
        Blob encrypted = Crypto.encryptWithManagedIV(AES256, key, data);
        String encryptedString  = EncodingUtil.base64Encode(encrypted);
        map<string,string> headerMap = new map<string,string>();
        headerMap.put('content-type', 'application/xml');
        headerMap.put('APIKey', +APIKey);

        InterfaceCallOutProcess callout = new InterfaceCallOutProcess(); 
		HTTPResponse resp = callout.doCallOut(custom_setting,request,encryptedString,headerMap,'','');
        
        return resp;
	}
    
    /**--------------------------------------------------------------------------------------------
    @description : leadPushToCRMNext method is used to create lead from sf to crmnext server
	-------------------------------------------------------------------------------------------------*/
	@future(callout = true)
    public static void leadPushToCRMNext(String leadStr){
        
        try{
        Map<Id,Lead> leadUpdateList =new Map<Id,Lead>();
        Map<String,interface_settings__c> interfaceCSstaticMap= null;
        if(interfaceCSstaticMap == null){
            interfaceCSstaticMap = InterfaceHandler.RetriveInterfaceSetting();
        }
        interface_settings__c objIntSetting = null;
        if(interfaceCSstaticMap <>null){
            objIntSetting = interfaceCSstaticMap.get(LEAD_CRMNEXT);
        }
        if(objIntSetting <> null && objIntSetting.CLOS_APIKey__c != null && objIntSetting.CLOS_Encription_Key__c != null){
            Blob key = Blob.valueof(objIntSetting.CLOS_Encription_Key__c);
            String APIKey = objIntSetting.CLOS_APIKey__c;
            List<Lead> lead = (List<Lead>) JSON.deserialize(leadStr, List<Lead>.class);
             Set<Id> productIds = new Set<Id>();
                for(Lead ld : lead){
                    System.debug('one lead '+ld);
                    System.debug(' leadname'+ld.Name);
                     System.debug('one lead product id'+ld.CLOS_Product_2__c);
                    System.debug('lead originator system'+ld.CLOS_Lead_Originator_System__c);
                    productIds.add(ld.CLOS_Product_2__c);
                }
                System.debug('Product Id '+productIds);
                List<CustomNotificationType> types = [SELECT Id FROM CustomNotificationType WHERE DeveloperName = 'Notify_Lead_Owner_Change'];
                Map<Id, Product2> productsMap = new Map<Id, Product2>([SELECT Id, Name FROM Product2 WHERE Id IN :productIds]);
            String requestedXML = null;
            String decryptedString = null;
            Lead leadData = lead[0];
            system.debug('**here print leadData.CLOS_CRMNEXT_API_Response__c'+leadData.CLOS_CRMNEXT_API_Response__c);
            if(leadData != null ){
                requestedXML = CLOS_LeadPushCRMNext_CC.generateCRMNextXML(leadData);
                System.debug('requestedXML ' +requestedXML);
                Lead leadUpdate = new Lead();
                leadUpdate.Id = leadData.Id;
                HTTPResponse resp;
                if(String.isNotBlank(requestedXML)){
                    resp = CLOS_LeadPushCRMNext_CC.callOutMethod(LEAD_CRMNEXT,METHOD,APIKey,key,requestedXML); 
                }
                if(resp != null){
                    Blob decrypted = Crypto.decryptWithManagedIV(AES256, key, EncodingUtil.base64Decode(resp.getBody()));
                    decryptedString = decrypted.toString();
                    System.debug('decryptedString ' +decryptedString);
                }
                else{    
                    leadUpdate.CLOS_CRMNEXT_API_Response__c = API_ERROR;
                    leadUpdateList.put(leadData.Id,leadUpdate);  
                }
                if(String.isNotBlank(decryptedString)){
                    Dom.Document crmnextRespXml = new Dom.Document();
                    crmnextRespXml.load(decryptedString);
                    
                    leadUpdate.CLOS_CRMNEXT_API_Response__c = REQUEST_ERROR;
                    for(Dom.XmlNode node :crmnextRespXml.getRootElement().getChildElements()){
                        if(node != null){
                            for(Dom.XmlNode nodeChild :node.getChildElements()){
                                
                                if(nodeChild.getName() == CRMLEADNUM && String.isNotBlank(nodeChild.getText())){
                                    
                                    leadUpdate.CLOS_CRMLeadNum__c = nodeChild.getText();
                                }
                                if(nodeChild.getName() == STATUS && String.isNotBlank(nodeChild.getText()) ){
                                    
                                    if(nodeChild.getText().trim() == SUCCESS){
                                        
                                        leadUpdate.CLOS_Lead_Destination_System__c = CRMNEXT;
                                        
                                    }
                                    leadUpdate.CLOS_CRMNEXT_API_Response__c = nodeChild.getText();
                                } 
                                
                                if( nodeChild.getText() != null && nodeChild.getName().trim() == ERRORCODE && String.isNotBlank(nodeChild.getText())){
                                
                                    leadUpdate.CLOS_CRMNEXT_API_Response__c = nodeChild.getText();
                                }  
                                
                                Messaging.CustomNotification notification = new Messaging.CustomNotification();
                                System.debug('lead originator'+leadUpdate.CLOS_Lead_Originator_System__c);
                                System.debug('lead destination system'+leadUpdate.CLOS_Lead_Destination_System__c);
                                
                                // notification.setTitle('Your Lead ' +ld.CLOS_SFLead_Id__c+ 'has been successfully assigned to '+ld.CLOS_Lead_Destination_System__c);
                                String notificationTitle='Your Lead ' +leadData.CLOS_SFLead_Id__c+ ' has been successfully assigned to CRMNext';
                                if(productsMap.containsKey(leadData.CLOS_Product_2__c) && productsMap.get(leadData.CLOS_Product_2__c).Name!=null) {
                                    notificationTitle+=' for ' + productsMap.get(leadData.CLOS_Product_2__c).Name + ' Product';
                                }
                                notification.setTitle(notificationTitle);
                                System.debug('notification** '+notification);
                                
                                System.debug('leadData.CLOS_SFLead_Id__c'+leadData.CLOS_SFLead_Id__c+' '+leadData.OwnerId);
                                notification.setBody('Lead Id: ' + leadData.CLOS_SFLead_Id__c);
                                notification.setSenderId(UserInfo.getUserId());
                                notification.setNotificationTypeId(types.get(0).Id); 
                                notification.setTargetId(leadData.Id);
                                notification.send(new Set<String>{leadData.OwnerId});
                            }
                        }
                    }
                    leadUpdateList.put(leadData.Id,leadUpdate); 
                }
            }
        }
        if(!leadUpdateList.isEmpty() && Lead.SObjectType.getDescribe().isAccessible() && Lead.sObjectType.getDescribe().isCreateable() &&  Lead.sObjectType.getDescribe().isUpdateable()){
            ELATF_TriggerOpsRecursionFlags.validationLeadFutureIsFirstRun = false;
            ELATF_TriggerOpsRecursionFlags.validationIsFirstRun = false;
            ELATF_TriggerOpsRecursionFlags.cont_Desc_IsFirstRun = false;
            ELATF_TriggerOpsRecursionFlags.updateCollateralFacilityRecords = false;
            ELATF_TriggerOpsRecursionFlags.assignExternalUserEnable = false;
            update leadUpdateList.values();
        }
        }
        catch(Exception ex){
            System.debug('ex 158 '+ex.getMessage() +' '+ex.getLineNumber());
        }
    }
    
    /**---------------------------------------------------------------------------------------------
    @description : generateCRMNextXML is used to create a xml file
	-------------------------------------------------------------------------------------------------*/
    public static void main(Lead leadData){
        String inputConversion = (leadData.CLOS_Input_Denomination__c != null) ? leadData.CLOS_Input_Denomination__c : 'Absolute' ;
        User userRec= [Select Id,Business_Unit__c From User Where Id = :leadData.CreatedById  WITH SECURITY_ENFORCED];
        String businessUnit = userRec.Business_Unit__c;
        String str = string.valueof(Math.abs(Crypto.getRandomLong()));
        String randomNumber = str.substring(0, 5);
        string body=XMLTAG;
        body = body+'<CreateLeadReq xmlns="http://www.kotak.com/schemas/AggregatorLeadCRM/CreateLeadReq">';
        body = body+'<Header>';
        body = body+'<SrcAppCd>CRMNext</SrcAppCd>';
        body = body+'<RequestID>'+randomNumber+'</RequestID>';
        body = body+'</Header>';
        body = body+'<CreateLead>';
        body = body+' <AssigntoCode></AssigntoCode>';
        if(String.isNotBlank(leadData.City)){
            body = body+'<City>'+leadData.City+'</City>';
        }else{
            body = body+'     <City />';
        }
        if(String.isNotBlank(leadData.Country)){
            body = body+' <Country>'+leadData.Country+'</Country>';
        }else{
            body = body+'     <Country />';
        }
        body = body+' <Custom>';
        if(String.isNotBlank(leadData.CLOS_Address_1__c)){
            body = body+' <Address_Line_1_Le>'+leadData.CLOS_Address_1__c+'</Address_Line_1_Le>';
        }else{
            body = body+'     <Address_Line_1_Le></Address_Line_1_Le>';
        }
        body = body+'     <Address_Line_P1_Le></Address_Line_P1_Le>';

        if(String.isNotBlank(leadData.CLOS_CRN_Number__c) ){
            body = body+'    <CRN>'+leadData.CLOS_CRN_Number__c+'</CRN>';
        }else{
            body = body+'     <CRN />';
        }
        body = body+'    <CampaignCode />';
        if(String.isNotBlank(leadData.CLOS_Name_of_Campaign__c )){
            body = body+'    <CampaignName>'+leadData.CLOS_Name_of_Campaign__c +'</CampaignName>';
        }else{
            body = body+'    <CampaignName></CampaignName>';
        }
        
       // body = body+'    <CampaignName />';
        if(String.isNotBlank(leadData.CLOS_Remarks__c)){
            body = body+'    <Remarks_Le>'+leadData.CLOS_Remarks__c+'</Remarks_Le>';
        }else{
            body = body+'    <Remarks_Le></Remarks_Le>';
        }
        if(String.isNotBlank(leadData.CLOS_Address_Type__c)){
            body = body+'    <Address_Type_Le>'+leadData.CLOS_Address_Type__c+'</Address_Type_Le>';
        }else{
            body = body+'    <Address_Type_Le></Address_Type_Le>';
        }
        
        body = body+'    <OutStndngLoanTnurInMnth></OutStndngLoanTnurInMnth>';
        body = body+'    <LoanTnurInMnth></LoanTnurInMnth>';
        body = body+'      <StrtmMnthYrOfCurntLoan></StrtmMnthYrOfCurntLoan>';
        body = body+'     <Landmark_LE></Landmark_LE>';
        body = body+'     <Source_SystemID>101</Source_SystemID>';
        body = body+'     <Process_Type>N</Process_Type>';

        if(String.isNotBlank(leadData.CLOS_Existing_Customer__c)){
            body = body+'     <Existing_Customer>'+leadData.CLOS_Existing_Customer__c+'</Existing_Customer>';
        }else{
            body = body+'     <Existing_Customer />';
        }
        body = body+'    <Resident_Type>R</Resident_Type>';
        
        if(String.isNotBlank(leadData.CLOS_Application_Type__c)){
            if(leadData.CLOS_Application_Type__c == INDIVIDUAL){
                leadData.CLOS_Application_Type__c = 'I';
            }else{
                leadData.CLOS_Application_Type__c = 'N';
            }
            body = body+'     <Applicant_Type>'+leadData.CLOS_Application_Type__c+'</Applicant_Type>';
        }else{
            body = body+'     <Applicant_Type />';
        }
        body = body+'     <RsiednceOrOfcPrmiseSelfOwnd />';
       /* if(String.isNotBlank(leadData.CLOS_Lead_Sub_Source__c)){
            body = body+'     <Sub_Source>Asset on Asset Cross Sell</Sub_Source>';//83228
        }else{
            body = body+'<Sub_Source></Sub_Source>';
        }*/
         body = body+'     <Sub_Source>Asset on Asset Cross Sell</Sub_Source>';//83228
        system.debug('businessUnit '+businessUnit);
        if(string.isNotBlank(businessUnit)){
       	body = body+'     <Sub_Sub_Source>CLOS '+businessUnit+'</Sub_Sub_Source>';//45001: CRMNext to SF LMS reverse update
        
        }body = body+'     <Perfios_Fetch_Applicable></Perfios_Fetch_Applicable>';
        body = body+'     <CurntBankLoanName></CurntBankLoanName>';
        body = body+'     <Work_Experience></Work_Experience>';
        body = body+'     <Propert_Identified></Propert_Identified>';
        body = body+'     <RateOfInterest_LE></RateOfInterest_LE>';
        body = body+'      <CoApplicant_Required></CoApplicant_Required>';
        body = body+'     <PermntAddSamAsCurnt></PermntAddSamAsCurnt>';
        body = body+'     <Country1_LE></Country1_LE>';
        body = body+'     <TypeOfProperty_LE />';
        body = body+'     <State1_LE/>';
       
        body = body+'     <District1_LE />';
        body = body+'    <City1_LE />';
        body = body+'<Pincode1_LE></Pincode1_LE>';
        body = body+'     <Landmark1_LE></Landmark1_LE>';
        body = body+'     <PermntAddLine_2></PermntAddLine_2>';
        body = body+'     <NetMonthlyIncome_LE></NetMonthlyIncome_LE>';
        body = body+'     <Outstanding_Loan_Amnt></Outstanding_Loan_Amnt>';
        //body = body+'     <CorrespondenceAddLine_2></CorrespondenceAddLine_2>';
        if(String.isNotBlank(leadData.CLOS_Address_2__c)){
            body = body+' <CorrespondenceAddLine_2>'+leadData.CLOS_Address_2__c+'</CorrespondenceAddLine_2>';
        }else{
            body = body+'     <CorrespondenceAddLine_2></CorrespondenceAddLine_2>';
        }
        body = body+'     <Cmpny_Emp_Name></Cmpny_Emp_Name>';
        body = body+'      <TenureInMonths_LE></TenureInMonths_LE>';
        if(String.isNotBlank(leadData.CLOS_customer_type__c)){
            if(leadData.CLOS_customer_type__c == INDIVIDUAL){
                leadData.CLOS_customer_type__c = 'I';
            }else{
                leadData.CLOS_customer_type__c = 'O';
            }
            body = body+'     <Individual>'+leadData.CLOS_customer_type__c+'</Individual>';
        }else{
            body = body+'<Individual></Individual>';
        }
        body = body+'     <Finfort_Fetch_Applicable></Finfort_Fetch_Applicable>';
        body = body+'     <EMI_Le></EMI_Le>';
        body = body+'     <Business_Le>Manufacturing</Business_Le>';
        body = body+'     <Vintage_Le></Vintage_Le>';
        if(leadData.CLOS_Turnover_Income_Details__c != null){
            leadData.CLOS_Turnover_Income_Details__c = Integer.valueOf(CLOS_UtilityClass.convertDenominationAndCurrency(inputConversion,'INR',leadData.CLOS_Turnover_Income_Details__c,'Absolute','INR',1));
            body = body+'  <Turnover_Le>'+leadData.CLOS_Turnover_Income_Details__c+'</Turnover_Le>';
        }else{
            body = body+'     <Turnover_Le></Turnover_Le>';
        }
       
        body = body+'     <property_Le>Residential</property_Le>';
        body = body+'      <LEA_Prospect>Lead</LEA_Prospect>';
        body = body+'  </Custom>';
        if(String.isNotBlank(leadData.Email)){
            body = body+'  <Email>'+leadData.Email+'</Email>';
        }
        // else{
        //     body = body+'<Email></Email>';
        // }
        if(String.isNotBlank(leadData.FirstName)){
            body = body+'  <FirstName>'+leadData.FirstName+'</FirstName>';
        }else{
            body = body+'     <FirstName></FirstName>';
        }
        
        if(String.isNotBlank(leadData.LastName)){
            body = body+'  <LastName>'+leadData.LastName+'</LastName>';
        }else{
            body = body+'     <LastName></LastName>';
        }
        if(String.isNotBlank(leadData.CLOS_Product_2__r.CLOS_LayoutKey__c	)){
            body = body+'     <LayoutKey>'+leadData.CLOS_Product_2__r.CLOS_LayoutKey__c+'</LayoutKey>';
        }else{
            body = body+'<LayoutKey></LayoutKey>';
        }

        System.debug('Loan Amount '+leadData.CLOS_Proposed_Loan_Amount__c);
        System.debug('Loan Amount Serialize '+JSON.Serialize(leadData.CLOS_Proposed_Loan_Amount__c));
        if(leadData.CLOS_Proposed_Loan_Amount__c != null){
            leadData.CLOS_Proposed_Loan_Amount__c = Integer.valueOf(CLOS_UtilityClass.convertDenominationAndCurrency(inputConversion,'INR',leadData.CLOS_Proposed_Loan_Amount__c,'Absolute','INR',1));

            System.debug(' CLOS_Proposed_Loan_Amount__c '+leadData.CLOS_Proposed_Loan_Amount__c);
            body = body+'  <LeadAmount>'+leadData.CLOS_Proposed_Loan_Amount__c+'</LeadAmount>';

        }
        else{
            body = body+'     <LeadAmount></LeadAmount>';
        }
        body = body+'  <LeadSourceKey>24</LeadSourceKey>';
        // if(String.isNotBlank(leadData.CLOS_Lead_Sourcing_Channel__c)){
        //     body = body+'  <LeadSourceKey>24</LeadSourceKey>';//83241
        // }else{
        //     body = body+'<LeadSourceKey></LeadSourceKey>';
        // }
        
        if(String.isNotBlank(leadData.MobilePhone)){
            body = body+'  <MobilePhone>'+leadData.MobilePhone+'</MobilePhone>';
        }else{
            body = body+'<MobilePhone></MobilePhone>';
        }
        
        if(String.isNotBlank(leadData.CLOS_Product_2__r.ProductCode)){
            body = body+'  <ProductKey>'+leadData.CLOS_Product_2__r.ProductCode+'</ProductKey>';
        }

        body = body+'  <RatingKey>2</RatingKey>';
        body = body+'  <SalutationKey></SalutationKey>';
        if(String.isNotBlank(leadData.State)){
            body = body+'  <State>'+leadData.State+'</State>';
        }
        else{
            body = body+'<State></State>';
        }
        body = body+'   <StatusCodeKey>100012</StatusCodeKey>';
        
        if(String.isNotBlank(leadData.PostalCode)){
            
          //  body = body+'  <Pincode1_LE>'+leadData.PostalCode+'</Pincode1_LE>';   
            body = body+'  <ZipCode>'+leadData.PostalCode+'</ZipCode>';   
        }
        else{
           // body = body+'<Pincode1_LE></Pincode1_LE>';
            body = body+'<ZipCode></ZipCode>';
        }
        body = body+'<LeadID></LeadID>';
        body = body+'</CreateLead>';
        body = body+'</CreateLeadReq>';    

        system.debug('request data '+body);
        system.debug('request data '+body);
       	//return body;
        System.Console.WriteLine(body)
    }
    
    /**---------------------------------------------------------------------------------------------
    @description : create xml for lead status update
	-------------------------------------------------------------------------------------------------*/
    public static String generateLeadStatusUpdateXML(Lead leadData){

        String str = string.valueof(Math.abs(Crypto.getRandomLong()));
        String randomNumber = str.substring(0, 2);

        
       /* DOM.Document newDoc=new DOM.Document();
        DOM.XmlNode lOSReverseStatusReq=newDoc.createRootElement('LOSReverseStatusReq',null,null);
        lOSReverseStatusReq.setAttribute('xmlns', 'https://www.kotak.com/schemas/LOSReverseStatusAPI/LOSReverseStatusReq');
        DOM.XmlNode header=lOSReverseStatusReq.addChildElement('Header',null,null);
        header.addChildElement('SrcAppCd',null,null).addTextNode('LOSCC');
        header.addChildElement('RequestID',null,null).addTextNode(randomNumber);

        DOM.XmlNode request=lOSReverseStatusReq.addChildElement('Request',null,null);
        if(leadData.CLOS_CRMLeadNum__c != null){
            request.addChildElement('CRMLeadNo',null,null).addTextNode(leadData.CLOS_CRMLeadNum__c);
        }else{
            request.addChildElement('CRMLeadNo',null,null).addTextNode('');
        }
        request.addChildElement('SZREJECT_CODE',null,null).addTextNode('');
        request.addChildElement('ISANC_AMT',null,null).addTextNode('');
        request.addChildElement('IDISB_AMT',null,null).addTextNode('');
        request.addChildElement('DTSANC_DATE',null,null).addTextNode('');
        request.addChildElement('DTDISB_DATE',null,null).addTextNode('');
        if(leadData.Status != null){
            request.addChildElement('SZSTATUS',null,null).addTextNode(leadData.Status);
        }else{
            request.addChildElement('SZSTATUS',null,null).addTextNode('');
        }

        
        return newDoc.toXmlString();*/

        string body=XMLTAG;
        body = body+'<LOSReverseStatusReq xmlns="http://www.kotak.com/schemas/LOSReverseStatusAPI/LOSReverseStatusReq">';
        body = body+'<Header>';
        body = body+'<SrcAppCd>KMBHL</SrcAppCd>';
        body = body+'<RequestID>'+randomNumber+'</RequestID>';
        body = body+'</Header>';
        body = body+'<Request>';
        if(leadData.CLOS_CRMLeadNum__c != null){
        body = body+' <CRMLeadNo>'+leadData.CLOS_CRMLeadNum__c+'</CRMLeadNo>';
        }
        body = body+' <SZREJECT_CODE></SZREJECT_CODE>';
        body = body+' <ISANC_AMT></ISANC_AMT>';
        body = body+' <IDISB_AMT></IDISB_AMT>';
        body = body+' <DTSANC_DATE></DTSANC_DATE>';
        body = body+' <DTDISB_DATE></DTDISB_DATE>';
        body = body+' <SZSTATUS></SZSTATUS>';        
        if(leadData.Status != null){
            body = body+' <SFStatusCode>'+leadData.Status+'</SFStatusCode>';
        }
        if(leadData.CLOS_Lead_Sub_Status__c != null){
        body = body+' <SFSubStatusCode>'+leadData.CLOS_Lead_Sub_Status__c+'</SFSubStatusCode>';//prod issue
        }
        body = body+'</Request>';
        body = body+'</LOSReverseStatusReq>';  

        return body;
    }
    
    /**---------------------------------------------------------------------------------------------
    @description : leadStatusUpdateToCRMNext method is used to create lead from sf to crmnext server
	-------------------------------------------------------------------------------------------------*/
	@future(callout = true)
    public static void leadStatusUpdateToCRMNext(String leadStr){
        Map<Id,Lead> leadUpdateList =new Map<Id,Lead>();
        Map<String,interface_settings__c> interfaceCSstaticMap = null;
        String decryptedString = null;
        if(interfaceCSstaticMap == null){
            interfaceCSstaticMap = InterfaceHandler.RetriveInterfaceSetting();
        }
        interface_settings__c objIntSetting = null ;
        if(interfaceCSstaticMap <>null){
            objIntSetting = interfaceCSstaticMap.get(CRMNEXT_STATUS);
        }
        if(objIntSetting <> null && objIntSetting.CLOS_APIKey__c != null && objIntSetting.CLOS_Encription_Key__c != null){
            Blob key = Blob.valueof(objIntSetting.CLOS_Encription_Key__c);
            String aPIKey = objIntSetting.CLOS_APIKey__c;
            List<Lead> lead = (List<Lead>) JSON.deserialize(leadStr, List<Lead>.class);
            String requestedXML = null;
            for(Lead leadData : lead){
                requestedXML = CLOS_LeadPushCRMNext_CC.generateLeadStatusUpdateXML(leadData);
                HTTPResponse resp;
                if(String.isNotBlank(requestedXML)){
                    resp = CLOS_LeadPushCRMNext_CC.callOutMethod(CRMNEXT_STATUS,METHOD,aPIKey,key,requestedXML); 
                }
                Lead leadUpdate = new Lead();
                leadUpdate.Id = leadData.Id;
                //System.debug('Resp ' +resp);
                if(resp != null){
                    //System.debug('Resp Body ' +resp.getBody());
                    //System.debug('Resp Status' +resp.getStatus());
                     Blob decrypted = Crypto.decryptWithManagedIV(AES256, key, EncodingUtil.base64Decode(resp.getBody()));
                     decryptedString = decrypted.toString();
                     //System.debug('decryptedString*** '+decryptedString);   
                }
                else{
                    leadUpdate.CLOS_CRMNEXT_API_Response__c = API_ERROR;
                    leadUpdateList.put(leadData.Id,leadUpdate);
                    
                }
                if(String.isNotBlank(decryptedString)){
                    Dom.Document crmnextRespXml = new Dom.Document();
                    crmnextRespXml.load(decryptedString);
                    
                    leadUpdate.CLOS_CRMNEXT_API_Response__c = REQUEST_ERROR;
                    for(Dom.XmlNode node :crmnextRespXml.getRootElement().getChildElements()){
                        if(node != null){
                            for(Dom.XmlNode nodeChild :node.getChildElements()){
                                
                                //System.debug('nodeChild** ' +nodeChild.getName());
                                if(nodeChild.getText() != null && nodeChild.getName().trim() == MESSAGE  && String.isNotBlank(nodeChild.getText())){
                                    leadUpdate.CLOS_CRMNEXT_API_Response__c = nodeChild.getText();
                                }
                                if(nodeChild.getText() != null && nodeChild.getName().trim() == ERRORCODE  && String.isNotBlank(nodeChild.getText())){
                                    leadUpdate.CLOS_CRMNEXT_API_Response__c = nodeChild.getText();
                                }

                            }
                        }
                    }
                    leadUpdateList.put(leadData.Id,leadUpdate);   
                }//close if
            }
        }

        if(!leadUpdateList.isEmpty() && Lead.SObjectType.getDescribe().isAccessible() && Lead.sObjectType.getDescribe().isCreateable() &&  Lead.sObjectType.getDescribe().isUpdateable()){
            ELATF_TriggerOpsRecursionFlags.validationLeadFutureIsFirstRun = false;
            ELATF_TriggerOpsRecursionFlags.validationIsFirstRun = false;
            ELATF_TriggerOpsRecursionFlags.cont_Desc_IsFirstRun = false;
            ELATF_TriggerOpsRecursionFlags.updateCollateralFacilityRecords = false;
            ELATF_TriggerOpsRecursionFlags.assignExternalUserEnable = false;
            update leadUpdateList.values();
        }
    }
    
}